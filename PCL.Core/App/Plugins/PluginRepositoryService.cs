using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using PCL.Core.App.Localization;
using PCL.Core.IO;
using PCL.Core.IO.Net;
using PCL.Core.IO.Net.Http;
using PCL.Core.Utils;

namespace PCL.Core.App.Plugins;

public sealed class PluginMarketQueryOptions
{
    public const int DefaultManifestSizeLimit = 1024 * 1024;

    public string Topic { get; set; } = "pclnexplugin";
    public string? GitHubToken { get; set; }
    public int PerPage { get; set; } = 100;
    public int MaxPages { get; set; } = 10;
    public int MaxManifestBytes { get; set; } = DefaultManifestSizeLimit;
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(15);
    public string? CacheDirectory { get; set; }
    public int? GitHubMirror { get; set; }
    public Architecture? Architecture { get; set; }
    public bool IncludeArchived { get; set; }
    public bool IncludeDisabled { get; set; }
    public bool IncludeForks { get; set; }
}

public sealed record PluginMarketError(string Repository, string Message);

public sealed record PluginMarketLoadResult(
    IReadOnlyList<PluginRepositoryEntry> Entries,
    IReadOnlyList<PluginMarketError> Errors,
    bool UsedRepositoryCache,
    bool RateLimited,
    DateTimeOffset? RateLimitReset)
{
    /// <summary>NexDeveloper 官方市场源声明的官方开发者。</summary>
    public IReadOnlyList<PluginDeveloperRecord> OfficialDevelopers { get; init; } = [];

    /// <summary>用户添加的第三方市场源声明的可信开发者 Login。</summary>
    public IReadOnlyList<string> TrustedDeveloperLogins { get; init; } = [];
}

/// <summary>
/// GitHub Topic based plugin market. Repositories are discovered through Search Repositories and
/// each repository's root manifest.json is authoritative for plugin metadata and downloads.
/// </summary>
public static class PluginRepositoryService
{
    public const int DefaultReadmeSizeLimit = 512 * 1024;
    private const string GitHubApiVersion = "2022-11-28";
    public const string OfficialMarketSourceUrl =
        "https://raw.githubusercontent.com/PCL-Nex-Developer/Nex_Server/refs/heads/main/apiv2/plugin-index.json";

    public static string BuildSearchUrl(int page, int perPage = 100, string topic = "pclnexplugin")
    {
        if (page <= 0) throw new ArgumentOutOfRangeException(nameof(page));
        if (perPage is <= 0 or > 100) throw new ArgumentOutOfRangeException(nameof(perPage));
        var query = Uri.EscapeDataString("topic:" + topic.Trim());
        return $"https://api.github.com/search/repositories?q={query}&sort=updated&order=desc&per_page={perPage}&page={page}";
    }

    public static string GetOfficialIndexUrl() => OfficialMarketSourceUrl;

    public static async Task<PluginMarketLoadResult> SearchTopicAsync(
        PluginMarketQueryOptions? options = null,
        HttpClient? httpClient = null,
        CancellationToken ct = default,
        IReadOnlySet<string>? skipRepositories = null)
    {
        options ??= new PluginMarketQueryOptions();
        ValidateOptions(options);
        httpClient ??= NetworkService.GetClient();

        var errors = new List<PluginMarketError>();
        List<GitHubRepository> repositories = [];
        var rateLimited = false;
        DateTimeOffset? rateLimitReset = null;

        try
        {
            repositories = await FetchRepositoriesAsync(options, httpClient, ct).ConfigureAwait(false);
        }
        catch (GitHubRateLimitException ex)
        {
            rateLimited = true;
            rateLimitReset = ex.Reset;
            errors.Add(new PluginMarketError("GitHub", ex.Message));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            errors.Add(new PluginMarketError("GitHub", ex.Message));
        }

        var architecture = options.Architecture ?? RuntimeInformation.OSArchitecture;
        var entries = new List<PluginRepositoryEntry>();
        foreach (var repository in repositories)
        {
            ct.ThrowIfCancellationRequested();
            if (skipRepositories is not null && skipRepositories.Contains(repository.FullName)) continue;
            if (!options.IncludeArchived && repository.Archived) continue;
            if (!options.IncludeDisabled && repository.Disabled) continue;
            if (!options.IncludeForks && repository.Fork) continue;

            try
            {
                var manifest = await FetchRepositoryManifestAsync(repository, options, httpClient, ct)
                    .ConfigureAwait(false);
                if (manifest is null)
                {
                    errors.Add(new PluginMarketError(repository.FullName, "Repository root manifest.json was not found or is inaccessible."));
                    continue;
                }
                var entry = CreateEntry(repository, manifest, architecture, options.Topic);
                var statistics = await FetchRepositoryStatisticsAsync(
                        repository, options, httpClient, ct)
                    .ConfigureAwait(false);
                entry.LastUpdatedAt = statistics?.ManifestUpdatedAt;
                entry.DownloadCount = statistics is { DownloadCount: > 0 }
                    ? statistics.DownloadCount
                    : null;
                entries.Add(entry);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                errors.Add(new PluginMarketError(repository.FullName, ex.Message));
            }
        }

        return new PluginMarketLoadResult(entries, errors, false, rateLimited, rateLimitReset);
    }

    private static async Task<List<GitHubRepository>> FetchRepositoriesAsync(
        PluginMarketQueryOptions options,
        HttpClient httpClient,
        CancellationToken ct)
    {
        var repositories = new List<GitHubRepository>();
        for (var page = 1; page <= options.MaxPages; page++)
        {
            ct.ThrowIfCancellationRequested();
            var url = BuildSearchUrl(page, options.PerPage, options.Topic);
            using var response = await SendGitHubAsync(url, "application/vnd.github+json", options, httpClient, ct)
                .ConfigureAwait(false);
            ThrowIfRateLimited(response);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<GitHubRepositorySearchResult>(PluginJson.SerializerOptions, ct)
                .ConfigureAwait(false) ?? new GitHubRepositorySearchResult();
            repositories.AddRange(result.Items);

            var cappedTotal = Math.Min(result.TotalCount, 1000);
            if (result.Items.Count < options.PerPage || repositories.Count >= cappedTotal) break;
        }

        return repositories
            .GroupBy(repository => repository.Id)
            .Select(group => group.First())
            .ToList();
    }

    private static async Task<PluginRepositoryStatistics?> FetchRepositoryStatisticsAsync(
        GitHubRepository repository,
        PluginMarketQueryOptions options,
        HttpClient httpClient,
        CancellationToken ct)
    {
        try
        {
            var updatedTask = FetchManifestUpdatedAtAsync(repository, options, httpClient, ct);
            var downloadsTask = FetchReleaseDownloadCountAsync(repository, options, httpClient, ct);
            await Task.WhenAll(updatedTask, downloadsTask).ConfigureAwait(false);
            return new PluginRepositoryStatistics
            {
                ManifestUpdatedAt = await updatedTask.ConfigureAwait(false),
                DownloadCount = await downloadsTask.ConfigureAwait(false)
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // 统计信息失败不影响插件条目本身（仅更新时间/下载数留空）。
            return null;
        }
    }

    private static async Task<DateTimeOffset?> FetchManifestUpdatedAtAsync(
        GitHubRepository repository,
        PluginMarketQueryOptions options,
        HttpClient httpClient,
        CancellationToken ct)
    {
        using var response = await SendGitHubAsync(
                BuildManifestCommitsApiUrl(repository),
                "application/vnd.github+json",
                options,
                httpClient,
                ct)
            .ConfigureAwait(false);
        ThrowIfRateLimited(response);
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Conflict) return null;
        response.EnsureSuccessStatusCode();
        var json = await ReadLimitedStringAsync(response, options.MaxManifestBytes, ct).ConfigureAwait(false);
        var commits = JsonSerializer.Deserialize<List<GitHubCommitSummary>>(json, PluginJson.SerializerOptions) ?? [];
        var commit = commits.FirstOrDefault()?.Commit;
        return commit?.Committer?.Date ?? commit?.Author?.Date;
    }

    private static async Task<long> FetchReleaseDownloadCountAsync(
        GitHubRepository repository,
        PluginMarketQueryOptions options,
        HttpClient httpClient,
        CancellationToken ct)
    {
        long total = 0;
        for (var page = 1; page <= options.MaxPages; page++)
        {
            using var response = await SendGitHubAsync(
                    BuildReleasesApiUrl(repository, page),
                    "application/vnd.github+json",
                    options,
                    httpClient,
                    ct)
                .ConfigureAwait(false);
            ThrowIfRateLimited(response);
            if (response.StatusCode == HttpStatusCode.NotFound) return total;
            response.EnsureSuccessStatusCode();
            var sizeLimit = Math.Max(options.MaxManifestBytes, 4 * 1024 * 1024);
            var json = await ReadLimitedStringAsync(response, sizeLimit, ct).ConfigureAwait(false);
            var releases = JsonSerializer.Deserialize<List<GitHubReleaseSummary>>(json, PluginJson.SerializerOptions) ?? [];
            foreach (var asset in releases.SelectMany(release => release.Assets ?? []))
                total += Math.Max(0, asset.DownloadCount);
            if (releases.Count < 100) break;
        }
        return total;
    }

    private static async Task<PluginMarketManifest?> FetchRepositoryManifestAsync(
        GitHubRepository repository,
        PluginMarketQueryOptions options,
        HttpClient httpClient,
        CancellationToken ct)
    {
        string json;
        try
        {
            // raw.githubusercontent.com 不受 GitHub 核心 API 配额限制。客户端每次打开商店
            // 都会为每个 topic 仓库抓取 manifest，走 contents API 会在几分钟内耗尽 60/hr 配额，
            // 导致所有插件在商店中消失。先走 raw，404（如默认分支变更）再回退到 contents API。
            using var response = await SendGitHubAsync(BuildRawManifestUrl(repository), "application/vnd.github.raw+json", options, httpClient, ct)
                .ConfigureAwait(false);
            ThrowIfRateLimited(response);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                using var apiResponse = await SendGitHubAsync(BuildManifestApiUrl(repository), "application/vnd.github.raw+json", options, httpClient, ct)
                    .ConfigureAwait(false);
                ThrowIfRateLimited(apiResponse);
                if (apiResponse.StatusCode == HttpStatusCode.NotFound) return null;
                apiResponse.EnsureSuccessStatusCode();
                json = await ReadLimitedStringAsync(apiResponse, options.MaxManifestBytes, ct).ConfigureAwait(false);
            }
            else
            {
                response.EnsureSuccessStatusCode();
                json = await ReadLimitedStringAsync(response, options.MaxManifestBytes, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }

        try
        {
            var manifest = JsonSerializer.Deserialize<PluginMarketManifest>(json, PluginJson.SerializerOptions)
                ?? throw new InvalidDataException("manifest.json is empty.");
            if (IsLegacyRepositoryVersionIndex(manifest))
            {
                var metadata = await FetchLegacyRepositoryMetadataAsync(repository, options, httpClient, ct)
                    .ConfigureAwait(false);
                NormalizeLegacyRepositoryVersionIndex(repository, manifest, metadata);
            }
            ValidateMarketManifest(manifest, repository.Owner.Login, repository.Name);
            return manifest;
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("manifest.json contains invalid JSON.", ex);
        }
    }

    private static PluginRepositoryEntry CreateEntry(
        GitHubRepository repository,
        PluginMarketManifest manifest,
        Architecture architecture,
        string topic)
    {
        ValidateMarketManifest(manifest, repository.Owner.Login, repository.Name);

        // Keep the latest declared version for display even when none of its packages match the
        // current architecture. The UI uses SelectedVersion + a null SelectedDownload to report
        // an explicit platform-incompatible state instead of incorrectly showing an unknown Core
        // compatibility state.
        var platform = GetCurrentPlatform();
        var version = SelectLatestVersion(manifest, platform, architecture) ?? SelectLatestVersion(manifest);
        var download = version is null ? null : SelectDownload(version, platform, architecture);
        if (version is not null && download is not null)
        {
            version.ResolvedPackageUrl = download.PackageUrl;
            version.ResolvedSha256 = download.Sha256;
        }
        var ownerLogin = repository.Owner.Login;
        var displayName = manifest.Author?.DisplayName;
        if (string.IsNullOrWhiteSpace(displayName)) displayName = ownerLogin;

        return new PluginRepositoryEntry
        {
            Id = manifest.Id.Trim(),
            Name = manifest.Name.Trim(),
            Description = manifest.Description?.Trim(),
            Readme = string.IsNullOrWhiteSpace(manifest.Readme) ? null : manifest.Readme.Trim(),
            ReadmeUrl = ResolveResourceUrl(manifest.ReadmeUrl, BuildRawManifestUrl(repository),
                BuildGitHubReadmeApiUrl(repository.FullName)),
            Author = displayName,
            GitHubLogin = ownerLogin,
            Version = version?.Version,
            ManifestUrl = BuildRawManifestUrl(repository),
            ManifestUrlIsDirect = true,
            HomepageUrl = string.IsNullOrWhiteSpace(manifest.HomepageUrl) ? repository.HtmlUrl : manifest.HomepageUrl,
            SourceRepoUrl = repository.HtmlUrl,
            Logo = ResolveLogoUrl(manifest.Logo, BuildRawManifestUrl(repository), repository.Owner.AvatarUrl),
            Tags = NormalizeTags((manifest.Tags ?? [])
                .Concat(repository.Topics ?? [])
                .Where(tag => !string.Equals(tag, topic, StringComparison.OrdinalIgnoreCase))),
            Group = manifest.Group,
            SourceKind = "GitHub",
            SourceGroup = topic,
            Archived = repository.Archived,
            Disabled = repository.Disabled,
            Fork = repository.Fork,
            MarketManifest = manifest,
            SelectedVersion = version,
            SelectedDownload = download,
            CompatibilityStatus = PluginCompatibility.EvaluatePclCoreVersion(version?.PclCoreVersion)
        };
    }

    public static PluginRepositoryEntry CreateManifestEntry(
        PluginMarketManifest manifest,
        string sourceUrl,
        Architecture? architecture = null,
        string sourceKind = "Manifest",
        string? sourceGroup = null,
        IEnumerable<string>? inheritedTags = null)
    {
        ValidateMarketManifest(manifest);
        var selectedArchitecture = architecture ?? RuntimeInformation.OSArchitecture;
        var indexedManifestUrl = manifest.Index?.ManifestUrl?.Trim();
        var effectiveManifestUrl = IsAbsoluteHttpUri(indexedManifestUrl) ? indexedManifestUrl! : sourceUrl;
        var platform = GetCurrentPlatform();
        var version = SelectLatestVersion(manifest, platform, selectedArchitecture) ?? SelectLatestVersion(manifest);
        var download = version is null ? null : SelectDownload(version, platform, selectedArchitecture);
        if (version is not null && download is not null)
        {
            version.ResolvedPackageUrl = download.PackageUrl;
            version.ResolvedSha256 = download.Sha256;
        }

        var repository = string.IsNullOrWhiteSpace(manifest.Repository) ? null : manifest.Repository.Trim();
        var tags = NormalizeTags((manifest.Tags ?? []).Concat(inheritedTags ?? []));
        var githubRepository = ParseGitHubRepositoryUrl(repository);
        var githubLogin = !string.IsNullOrWhiteSpace(manifest.Author?.GitHubLogin)
            ? manifest.Author.GitHubLogin.Trim()
            : githubRepository?.Owner;
        var fallbackLogo = githubRepository is not null && !string.IsNullOrWhiteSpace(githubLogin)
            ? $"https://github.com/{Uri.EscapeDataString(githubLogin)}.png?size=128"
            : null;
        var fallbackReadme = githubRepository is null
            ? null
            : BuildGitHubReadmeApiUrl(githubRepository.Owner + "/" + githubRepository.Name);
        return new PluginRepositoryEntry
        {
            Id = manifest.Id.Trim(),
            Name = manifest.Name.Trim(),
            Description = manifest.Description?.Trim(),
            Readme = string.IsNullOrWhiteSpace(manifest.Readme) ? null : manifest.Readme.Trim(),
            ReadmeUrl = ResolveResourceUrl(manifest.ReadmeUrl, effectiveManifestUrl, fallbackReadme),
            Author = string.IsNullOrWhiteSpace(manifest.Author?.DisplayName)
                ? githubLogin
                : manifest.Author.DisplayName.Trim(),
            GitHubLogin = githubLogin,
            Version = version?.Version,
            ManifestUrl = effectiveManifestUrl,
            ManifestUrlIsDirect = IsAbsoluteHttpUri(indexedManifestUrl)
                                  || string.Equals(sourceKind, "Manifest", StringComparison.OrdinalIgnoreCase),
            HomepageUrl = string.IsNullOrWhiteSpace(manifest.HomepageUrl) ? repository : manifest.HomepageUrl.Trim(),
            SourceRepoUrl = repository,
            Logo = ResolveLogoUrl(manifest.Logo, effectiveManifestUrl, fallbackLogo),
            Tags = tags,
            Group = string.IsNullOrWhiteSpace(manifest.Group) ? null : manifest.Group.Trim(),
            SourceKind = sourceKind,
            SourceGroup = string.IsNullOrWhiteSpace(sourceGroup) ? GetSourceLabel(sourceUrl) : sourceGroup.Trim(),
            MarketManifest = manifest,
            SelectedVersion = version,
            SelectedDownload = download,
            CompatibilityStatus = PluginCompatibility.EvaluatePclCoreVersion(version?.PclCoreVersion),
            LastUpdatedAt = manifest.Index?.LastUpdatedAt,
            DownloadCount = manifest.Index is { DownloadCount: > 0 } ? manifest.Index.DownloadCount : null,
            Archived = manifest.Index?.Archived ?? false,
            Disabled = manifest.Index?.Disabled ?? false,
            Fork = manifest.Index?.Fork ?? false
        };
    }

    public static PluginMarketVersion? SelectLatestVersion(PluginMarketManifest manifest)
        => SelectLatestVersion(manifest, null);

    public static IReadOnlyList<PluginMarketVersion> GetVersionsNewestFirst(PluginMarketManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        return (manifest.Versions ?? [])
            .Select((version, index) => new { Version = version, Index = index, Parsed = ParsePluginVersion(version.Version) })
            .Where(item => item.Parsed is not null)
            .OrderByDescending(item => item.Parsed)
            .ThenBy(item => item.Index)
            .Select(item => item.Version)
            .ToArray();
    }

    public static PluginMarketVersion? SelectLatestVersion(PluginMarketManifest manifest, Architecture? architecture)
        => SelectLatestVersion(manifest, GetCurrentPlatform(), architecture);

    public static PluginMarketVersion? SelectLatestVersion(
        PluginMarketManifest manifest,
        OSPlatform platform,
        Architecture? architecture)
    {
        var candidates = (manifest.Versions ?? [])
            .Select((version, index) => new { Version = version, Index = index, Parsed = ParsePluginVersion(version.Version) })
            .Where(item => item.Parsed is not null)
            .Where(item => architecture is null || SelectDownload(item.Version, platform, architecture.Value) is not null)
            .OrderByDescending(item => item.Parsed)
            .ThenBy(item => item.Index)
            .Select(item => item.Version)
            .ToArray();

        if (architecture is null) return candidates.FirstOrDefault();
        return candidates.FirstOrDefault(version =>
                   PluginCompatibility.EvaluatePclCoreVersion(version.PclCoreVersion)
                   != PluginCoreCompatibilityStatus.TooOld)
               ?? candidates.FirstOrDefault();
    }

    public static PluginMarketDownload? SelectDownload(PluginMarketVersion version, Architecture architecture)
        => SelectDownload(version, GetCurrentPlatform(), architecture);

    public static PluginMarketDownload? SelectDownload(
        PluginMarketVersion version,
        OSPlatform platform,
        Architecture architecture)
    {
        var downloads = version.Downloads;
        var platformDownloads = GetPlatformDownloads(downloads, platform);
        PluginMarketDownload? selected = SelectArchitectureDownload(platformDownloads, architecture);

        // A declared OS group is authoritative. Legacy keys are consulted only when that whole
        // group is absent, preventing native packages for another OS from being selected.
        if (platformDownloads is null)
        {
            selected = architecture switch
            {
                Architecture.X64 => downloads?.Amd64 ?? downloads?.AnyCpu,
                Architecture.Arm64 => downloads?.Arm64 ?? downloads?.AnyCpu,
                _ => downloads?.AnyCpu
            };
        }

        return selected is not null && IsGeneralPackageUrl(selected.PackageUrl) && IsValidSha256(selected.Sha256)
            ? selected
            : null;
    }

    private static OSPlatform GetCurrentPlatform()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return OSPlatform.Windows;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return OSPlatform.Linux;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return OSPlatform.OSX;
        return OSPlatform.Create("unknown");
    }

    private static PluginMarketArchitectureDownloads? GetPlatformDownloads(
        PluginMarketDownloads? downloads,
        OSPlatform platform)
    {
        if (platform.Equals(OSPlatform.Windows)) return downloads?.Windows;
        if (platform.Equals(OSPlatform.Linux)) return downloads?.Linux;
        if (platform.Equals(OSPlatform.OSX)) return downloads?.MacOS;
        return null;
    }

    private static PluginMarketDownload? SelectArchitectureDownload(
        PluginMarketArchitectureDownloads? downloads,
        Architecture architecture)
        => architecture switch
        {
            Architecture.X64 => downloads?.Amd64 ?? downloads?.AnyCpu,
            Architecture.Arm64 => downloads?.Arm64 ?? downloads?.AnyCpu,
            _ => downloads?.AnyCpu
        };

    public static bool IsValidSha256(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var text = value.Trim();
        return text.Length == 64 && text.All(character =>
            character is >= '0' and <= '9'
            or >= 'a' and <= 'f'
            or >= 'A' and <= 'F');
    }

    public static void ValidateManifestDownloads(PluginMarketManifest manifest)
    {
        var repository = ParseGitHubRepositoryUrl(manifest.Repository);
        var versions = manifest.Versions ?? [];
        if (versions.Count == 0) throw new InvalidDataException("manifest.json has no plugin versions.");
        foreach (var version in versions)
        {
            if (ParsePluginVersion(version.Version) is null)
                throw new InvalidDataException($"Plugin version {version.Version ?? "?"} is not valid SemVer.");
            // Missing or malformed pclCoreVersion is a supported market state. Keep the entry so
            // the UI can display Unknown and require confirmation at install/enable time.
            if (ContainsLegacyDownloadFields(version))
                throw new InvalidDataException($"Plugin version {version.Version ?? "?"} must declare packages only under downloads.");
            var downloads = version.Downloads;
            if (downloads is null)
                throw new InvalidDataException($"Plugin version {version.Version ?? "?"} has no downloads.");

            ValidateArchitectureGroup(downloads.Windows, "windows", version.Version);
            ValidateArchitectureGroup(downloads.Linux, "linux", version.Version);
            ValidateArchitectureGroup(downloads.MacOS, "macos", version.Version);

            var declared = new[]
                {
                    downloads.Amd64, downloads.Arm64, downloads.AnyCpu,
                    downloads.Windows?.Amd64, downloads.Windows?.Arm64, downloads.Windows?.AnyCpu,
                    downloads.Linux?.Amd64, downloads.Linux?.Arm64, downloads.Linux?.AnyCpu,
                    downloads.MacOS?.Amd64, downloads.MacOS?.Arm64, downloads.MacOS?.AnyCpu
                }
                .Where(download => download is not null)
                .Cast<PluginMarketDownload>()
                .ToArray();
            if (declared.Length == 0)
                throw new InvalidDataException($"Plugin version {version.Version ?? "?"} has no downloads.");
            if (repository is not null)
            {
                var releaseTag = ValidateReleaseNotes(version.ReleaseNotes, repository, version.Version!);
                foreach (var download in declared)
                    ValidateDownload(download.PackageUrl, download.Sha256, version.Version, repository, releaseTag);
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(version.ReleaseNotes) && !IsAbsoluteHttpUri(version.ReleaseNotes))
                    throw new InvalidDataException($"Plugin version {version.Version} has an invalid releaseNotes URL.");
                foreach (var download in declared)
                {
                    if (!IsGeneralPackageUrl(download.PackageUrl))
                        throw new InvalidDataException($"Plugin version {version.Version} must provide a complete HTTP/HTTPS .pclx packageUrl.");
                    if (!IsValidSha256(download.Sha256))
                        throw new InvalidDataException($"Plugin version {version.Version} must provide a valid 64-hex SHA-256.");
                }
            }
        }
    }

    private static void ValidateArchitectureGroup(
        PluginMarketArchitectureDownloads? downloads,
        string platform,
        string? version)
    {
        if (downloads is not null
            && downloads.Amd64 is null
            && downloads.Arm64 is null
            && downloads.AnyCpu is null)
            throw new InvalidDataException(
                $"Plugin version {version ?? "?"} has an empty downloads.{platform} group.");
    }

    public static void ValidateMarketManifest(PluginMarketManifest manifest)
        => ValidateMarketManifest(manifest, null, null);

    private static void ValidateMarketManifest(
        PluginMarketManifest manifest,
        string? authoritativeOwner,
        string? authoritativeRepository)
    {
        if (string.IsNullOrWhiteSpace(manifest.Id))
            throw new InvalidDataException("manifest.json is missing id.");
        if (!PluginPackageService.IsValidPluginId(manifest.Id))
            throw new InvalidDataException("manifest.json contains an invalid plugin id.");
        if (string.IsNullOrWhiteSpace(manifest.Name))
            throw new InvalidDataException("manifest.json is missing name.");
        if (manifest.Author is null
            || string.IsNullOrWhiteSpace(manifest.Author.DisplayName) && string.IsNullOrWhiteSpace(manifest.Author.GitHubLogin))
            throw new InvalidDataException("manifest.json must declare author.displayName or author.githubLogin.");
        if (!string.IsNullOrWhiteSpace(manifest.Author.GitHubLogin) && !IsValidGitHubLogin(manifest.Author.GitHubLogin))
            throw new InvalidDataException("manifest.json contains an invalid author.githubLogin.");
        if (string.IsNullOrWhiteSpace(manifest.Description))
            throw new InvalidDataException("manifest.json is missing description.");
        var repository = ParseGitHubRepositoryUrl(manifest.Repository);
        if (!string.IsNullOrWhiteSpace(manifest.Repository) && repository is null && !IsAbsoluteHttpUri(manifest.Repository))
            throw new InvalidDataException("manifest.json repository must be a complete HTTP/HTTPS URL.");
        if (repository is not null
            && !string.IsNullOrWhiteSpace(manifest.Author.GitHubLogin)
            && !string.Equals(manifest.Author.GitHubLogin, repository.Owner, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("GitHub manifest author.githubLogin must match the repository owner.");
        if (authoritativeOwner is not null
            && (repository is null
                || !string.Equals(repository.Owner, authoritativeOwner, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(repository.Name, authoritativeRepository, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("manifest.json repository must match the GitHub Topic repository.");
        var dependencyValidation = PluginDependencyService.ValidateDeclarations(manifest.Id, manifest.Dependencies);
        if (!dependencyValidation.IsValid)
            throw new InvalidDataException("manifest.json dependencies are invalid: " + dependencyValidation.ErrorMessage);
        ValidateManifestDownloads(manifest);
        foreach (var version in manifest.Versions)
        {
            var resolvedDependencies = version.Dependencies ?? manifest.Dependencies;
            dependencyValidation = PluginDependencyService.ValidateDeclarations(manifest.Id, resolvedDependencies);
            if (!dependencyValidation.IsValid)
                throw new InvalidDataException(
                    $"manifest.json version {version.Version ?? "?"} dependencies are invalid: {dependencyValidation.ErrorMessage}");
            version.PluginId = manifest.Id.Trim();
            version.ResolvedDependencies = resolvedDependencies.ToArray();
        }
    }

    private static void ValidateDownload(
        string? packageUrl,
        string? sha256,
        string? version,
        GitHubRepositoryIdentity repository,
        string releaseTag)
    {
        var packageTag = ParseGitHubReleasePackageUrl(packageUrl, repository);
        if (packageTag is null || !string.Equals(packageTag, releaseTag, StringComparison.Ordinal))
            throw new InvalidDataException($"Plugin version {version ?? "?"} has an invalid GitHub Release .pclx packageUrl.");
        if (!IsValidSha256(sha256))
            throw new InvalidDataException($"Plugin version {version ?? "?"} must provide a valid 64-hex SHA-256.");
    }

    private static bool ContainsLegacyDownloadFields(PluginMarketVersion version)
        => version.AdditionalProperties?.Keys.Any(key =>
            string.Equals(key, "packageUrl", StringComparison.OrdinalIgnoreCase)
            || string.Equals(key, "sha256", StringComparison.OrdinalIgnoreCase)) == true;

    private static bool IsLegacyRepositoryVersionIndex(PluginMarketManifest manifest)
    {
        if (!string.IsNullOrWhiteSpace(manifest.Id)
            || !string.IsNullOrWhiteSpace(manifest.Name)
            || manifest.Author is not null
            || !string.IsNullOrWhiteSpace(manifest.Description)
            || !string.IsNullOrWhiteSpace(manifest.Repository))
            return false;

        return manifest.Versions is { Count: > 0 }
               && manifest.Versions.Any(version =>
                   version.Downloads is null && TryGetLegacyVersionString(version, "packageUrl", out _));
    }

    private static async Task<LegacyRepositoryPluginMetadata?> FetchLegacyRepositoryMetadataAsync(
        GitHubRepository repository,
        PluginMarketQueryOptions options,
        HttpClient httpClient,
        CancellationToken ct)
    {
        try
        {
            using var response = await SendGitHubAsync(
                    BuildRepositoryFileApiUrl(repository, "plugin.json"),
                    "application/vnd.github.raw+json",
                    options,
                    httpClient,
                    ct)
                .ConfigureAwait(false);
            ThrowIfRateLimited(response);
            if (response.StatusCode == HttpStatusCode.NotFound) return null;
            response.EnsureSuccessStatusCode();
            var json = await ReadLimitedStringAsync(response, options.MaxManifestBytes, ct).ConfigureAwait(false);
            return JsonSerializer.Deserialize<LegacyRepositoryPluginMetadata>(json, PluginJson.SerializerOptions);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private static void NormalizeLegacyRepositoryVersionIndex(
        GitHubRepository repository,
        PluginMarketManifest manifest,
        LegacyRepositoryPluginMetadata? metadata)
    {
        manifest.Id = !string.IsNullOrWhiteSpace(metadata?.Id)
            ? metadata.Id.Trim()
            : InferLegacyPluginId(manifest.Versions) ?? BuildRepositoryFallbackPluginId(repository);
        manifest.Name = !string.IsNullOrWhiteSpace(metadata?.Name)
            ? metadata.Name.Trim()
            : repository.Name;
        manifest.Author = new PluginMarketAuthor
        {
            GitHubLogin = repository.Owner.Login,
            DisplayName = GetLegacyAuthorDisplayName(metadata?.Author) ?? repository.Owner.Login
        };
        manifest.Description = !string.IsNullOrWhiteSpace(metadata?.Description)
            ? metadata.Description.Trim()
            : !string.IsNullOrWhiteSpace(repository.Description)
                ? repository.Description.Trim()
                : repository.Name;
        manifest.Repository = repository.HtmlUrl;
        if (string.IsNullOrWhiteSpace(manifest.Logo) && !string.IsNullOrWhiteSpace(metadata?.Logo))
            manifest.Logo = metadata.Logo.Trim();

        foreach (var version in manifest.Versions)
        {
            if (version.Downloads is not null
                || !TryGetLegacyVersionString(version, "packageUrl", out var packageUrl))
                continue;

            TryGetLegacyVersionString(version, "sha256", out var sha256);
            version.Downloads = new PluginMarketDownloads
            {
                AnyCpu = new PluginMarketDownload
                {
                    PackageUrl = packageUrl,
                    Sha256 = sha256
                }
            };
            version.IsLegacyRepositoryVersion = true;
            RemoveLegacyVersionProperty(version, "packageUrl");
            RemoveLegacyVersionProperty(version, "sha256");
        }
    }

    private static bool TryGetLegacyVersionString(
        PluginMarketVersion version,
        string propertyName,
        out string value)
    {
        value = string.Empty;
        if (version.AdditionalProperties is null) return false;
        var property = version.AdditionalProperties.FirstOrDefault(pair =>
            string.Equals(pair.Key, propertyName, StringComparison.OrdinalIgnoreCase));
        if (property.Key is null || property.Value.ValueKind != JsonValueKind.String) return false;
        value = property.Value.GetString()?.Trim() ?? string.Empty;
        return value.Length > 0;
    }

    private static void RemoveLegacyVersionProperty(PluginMarketVersion version, string propertyName)
    {
        if (version.AdditionalProperties is null) return;
        var key = version.AdditionalProperties.Keys.FirstOrDefault(candidate =>
            string.Equals(candidate, propertyName, StringComparison.OrdinalIgnoreCase));
        if (key is not null) version.AdditionalProperties.Remove(key);
    }

    private static string? InferLegacyPluginId(IEnumerable<PluginMarketVersion> versions)
    {
        foreach (var version in versions)
        {
            if (!TryGetLegacyVersionString(version, "packageUrl", out var packageUrl)
                || !Uri.TryCreate(packageUrl, UriKind.Absolute, out var uri))
                continue;

            var fileName = Path.GetFileNameWithoutExtension(uri.AbsolutePath);
            if (string.IsNullOrWhiteSpace(fileName)) continue;
            var suffixes = string.IsNullOrWhiteSpace(version.Version)
                ? Array.Empty<string>()
                : new[] { "-v" + version.Version.Trim(), "-" + version.Version.Trim() };
            foreach (var suffix in suffixes)
            {
                if (!fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) continue;
                var candidate = fileName[..^suffix.Length];
                if (PluginPackageService.IsValidPluginId(candidate)) return candidate;
            }
        }

        return null;
    }

    private static string BuildRepositoryFallbackPluginId(GitHubRepository repository)
        => "github." + SanitizePluginIdSegment(repository.Owner.Login) + "." + SanitizePluginIdSegment(repository.Name);

    private static string SanitizePluginIdSegment(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            var valid = character is >= 'A' and <= 'Z'
                or >= 'a' and <= 'z'
                or >= '0' and <= '9'
                or '_' or '-';
            builder.Append(valid ? character : '-');
        }
        var result = builder.ToString().Trim('-', '_');
        return result.Length == 0 ? "plugin" : result;
    }

    private static string? GetLegacyAuthorDisplayName(JsonElement? author)
    {
        if (author is null) return null;
        var value = author.Value;
        if (value.ValueKind == JsonValueKind.String) return value.GetString()?.Trim();
        if (value.ValueKind != JsonValueKind.Object) return null;
        foreach (var propertyName in new[] { "displayName", "name", "githubLogin" })
        {
            if (value.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String)
            {
                var text = property.GetString()?.Trim();
                if (!string.IsNullOrWhiteSpace(text)) return text;
            }
        }
        return null;
    }

    private static bool IsValidGitHubLogin(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var login = value.Trim();
        return login.Length <= 39
               && login[0] != '-'
               && login[^1] != '-'
               && !login.Contains("--", StringComparison.Ordinal)
               && login.All(character => char.IsAsciiLetterOrDigit(character) || character == '-');
    }

    private static GitHubRepositoryIdentity? ParseGitHubRepositoryUrl(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase)
            || !uri.IsDefaultPort
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment)
            || !string.IsNullOrEmpty(uri.UserInfo)) return null;

        var segments = SplitPath(uri);
        if (segments.Length != 2 || !IsValidGitHubLogin(segments[0]) || string.IsNullOrWhiteSpace(segments[1])) return null;
        var name = segments[1];
        if (name.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) return null;
        return new GitHubRepositoryIdentity(segments[0], name);
    }

    /// <summary>
    /// 从仓库 URL 解析 "owner/name"。用于把已由官方索引提供的仓库从实时搜索中排除。
    /// </summary>
    public static string? ParseRepositoryFullName(string? repositoryUrl)
        => ParseGitHubRepositoryUrl(repositoryUrl) is { } identity
            ? $"{identity.Owner}/{identity.Name}"
            : null;

    private static string ValidateReleaseNotes(
        string? value,
        GitHubRepositoryIdentity repository,
        string version)
    {
        if (!TryParseGitHubUrl(value, out var segments)
            || segments.Length != 5
            || !RepositoryMatches(segments, repository)
            || !string.Equals(segments[2], "releases", StringComparison.Ordinal)
            || !string.Equals(segments[3], "tag", StringComparison.Ordinal)
            || !IsVersionReleaseTag(segments[4], version))
            throw new InvalidDataException($"Plugin version {version} has an invalid GitHub Release releaseNotes URL.");
        return segments[4];
    }

    private static string? ParseGitHubReleasePackageUrl(string? value, GitHubRepositoryIdentity repository)
    {
        if (!TryParseGitHubUrl(value, out var segments)
            || segments.Length != 6
            || !RepositoryMatches(segments, repository)
            || !string.Equals(segments[2], "releases", StringComparison.Ordinal)
            || !string.Equals(segments[3], "download", StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(segments[4])
            || !segments[5].EndsWith(".pclx", StringComparison.OrdinalIgnoreCase)) return null;
        return segments[4];
    }

    private static bool TryParseGitHubUrl(string? value, out string[] segments)
    {
        segments = [];
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase)
            || !uri.IsDefaultPort
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment)
            || !string.IsNullOrEmpty(uri.UserInfo)) return false;
        segments = SplitPath(uri);
        return true;
    }

    private static string[] SplitPath(Uri uri)
        => uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(Uri.UnescapeDataString)
            .ToArray();

    private static bool RepositoryMatches(string[] segments, GitHubRepositoryIdentity repository)
        => segments.Length >= 2
           && string.Equals(segments[0], repository.Owner, StringComparison.OrdinalIgnoreCase)
           && string.Equals(segments[1], repository.Name, StringComparison.OrdinalIgnoreCase);

    private static bool IsVersionReleaseTag(string tag, string version)
        => string.Equals(tag, version, StringComparison.Ordinal)
           || string.Equals(tag, "v" + version, StringComparison.Ordinal);

    private sealed record GitHubRepositoryIdentity(string Owner, string Name);

    public static IEnumerable<PluginInstallSourceEntry> GetInstallSources(PluginRepositoryEntry entry)
    {
        if (entry.SelectedDownload is { } download && IsGeneralPackageUrl(download.PackageUrl))
        {
            yield return new PluginInstallSourceEntry
            {
                Type = "package",
                Name = "Release",
                Url = download.PackageUrl.Trim(),
                Sha256 = download.Sha256
            };
            yield break;
        }

        if (entry.SelectedVersion is not null) yield break;

        if (IsAbsoluteHttpUri(entry.ManifestUrl))
            yield return new PluginInstallSourceEntry { Type = "manifest", Name = "Manifest", Url = entry.ManifestUrl!.Trim() };
    }

    public static (PluginInstallSourceType Type, string Url) GetPersistentInstallSource(
        PluginRepositoryEntry entry,
        PluginInstallSourceEntry selected,
        PluginInstallSourceType fallbackType,
        string fallbackUrl)
    {
        if (string.Equals(selected.Type, "git", StringComparison.OrdinalIgnoreCase))
            return (PluginInstallSourceType.Git, selected.Url.Trim());
        if (IsGitRepositoryUrl(entry.SourceRepoUrl))
            return (PluginInstallSourceType.Git, entry.SourceRepoUrl!.Trim());
        if (!string.IsNullOrWhiteSpace(entry.ManifestUrl))
            return (PluginInstallSourceType.Manifest, entry.ManifestUrl.Trim());
        return (fallbackType, fallbackUrl);
    }

    // Compatibility wrapper for custom legacy repository indexes.
    public static async Task<PluginRepositoryIndex?> FetchIndexAsync(string url, CancellationToken ct = default)
    {
        if (url.StartsWith("https://api.github.com/search/repositories", StringComparison.OrdinalIgnoreCase))
        {
            var result = await SearchTopicAsync(ct: ct).ConfigureAwait(false);
            return new PluginRepositoryIndex { Name = "GitHub Topic: pclnexplugin", Plugins = result.Entries.ToList() };
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            using var response = await HttpRequest.Create(url).SendAsync(cancellationToken: timeout.Token).ConfigureAwait(false);
            var index = await response.AsJsonAsync<PluginRepositoryIndex>(PluginJson.SerializerOptions, timeout.Token).ConfigureAwait(false);
            if (index is null) return null;
            NormalizeIndex(index, url);
            return index;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { return null; }
    }

    public static IReadOnlyList<PluginRepositoryEntry> MergeIndexes(IReadOnlyList<PluginRepositoryIndex> indexes)
    {
        return indexes.SelectMany(index => index.Plugins ?? [])
            .Where(entry => entry is not null && !string.IsNullOrWhiteSpace(entry.Id))
            .GroupBy(entry => $"{entry.Id}|{entry.ManifestUrl ?? string.Empty}|{entry.SourceRepoUrl ?? string.Empty}",
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
    }

    public static void NormalizeIndex(PluginRepositoryIndex index, string sourceUrl)
    {
        foreach (var entry in index.Plugins ?? [])
        {
            if (entry is null) continue;
            entry.Id = entry.Id?.Trim() ?? string.Empty;
            entry.Name = entry.Name?.Trim() ?? string.Empty;
            entry.Version = string.IsNullOrWhiteSpace(entry.Version) ? null : entry.Version.Trim();
            entry.Author = string.IsNullOrWhiteSpace(entry.Author) ? null : entry.Author.Trim();
            entry.Description = string.IsNullOrWhiteSpace(entry.Description) ? null : entry.Description.Trim();
            entry.Readme = string.IsNullOrWhiteSpace(entry.Readme) ? null : entry.Readme.Trim();
            entry.ReadmeUrl = ResolveResourceUrl(entry.ReadmeUrl, sourceUrl, null);
            entry.ManifestUrl = string.IsNullOrWhiteSpace(entry.ManifestUrl) ? null : entry.ManifestUrl.Trim();
            entry.ManifestUrlIsDirect = !string.IsNullOrWhiteSpace(entry.ManifestUrl);
            entry.SourceRepoUrl = string.IsNullOrWhiteSpace(entry.SourceRepoUrl) ? sourceUrl : entry.SourceRepoUrl.Trim();
            entry.HomepageUrl ??= entry.Homepage;
            entry.HomepageUrl = string.IsNullOrWhiteSpace(entry.HomepageUrl) ? null : entry.HomepageUrl.Trim();
            entry.Homepage = string.IsNullOrWhiteSpace(entry.Homepage) ? entry.HomepageUrl : entry.Homepage.Trim();
        }

        index.Plugins = (index.Plugins ?? [])
            .Where(entry => entry is not null
                            && !string.IsNullOrWhiteSpace(entry.Id)
                            && !string.IsNullOrWhiteSpace(entry.Name)
                            && IsAbsoluteHttpUri(entry.ManifestUrl))
            .GroupBy(entry => entry.ManifestUrl!, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
    }

    public static async Task<string?> FetchReadmeAsync(
        PluginRepositoryEntry entry,
        HttpClient? httpClient = null,
        string? cacheDirectory = null,
        int maxBytes = DefaultReadmeSizeLimit,
        string? githubToken = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (maxBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maxBytes));
        var inlineReadme = string.IsNullOrWhiteSpace(entry.Readme)
            ? entry.MarketManifest?.Readme
            : entry.Readme;
        if (!string.IsNullOrWhiteSpace(inlineReadme)) return inlineReadme.Trim();

        var location = entry.ReadmeUrl;
        if (string.IsNullOrWhiteSpace(location) && !string.IsNullOrWhiteSpace(entry.MarketManifest?.ReadmeUrl))
            location = ResolveResourceUrl(entry.MarketManifest.ReadmeUrl, entry.ManifestUrl ?? string.Empty, null);
        if (string.IsNullOrWhiteSpace(location))
        {
            var repository = ParseGitHubRepositoryUrl(entry.SourceRepoUrl);
            if (repository is not null)
                location = BuildGitHubReadmeApiUrl(repository.Owner + "/" + repository.Name);
        }
        if (string.IsNullOrWhiteSpace(location)) return null;

        if (File.Exists(location))
        {
            var info = new FileInfo(location);
            if (info.Length > maxBytes) throw new InvalidDataException(Lang.Text("Plugins.Repository.Error.ReadmeTooLarge"));
            return await File.ReadAllTextAsync(location, ct).ConfigureAwait(false);
        }

        if (!Uri.TryCreate(location, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https")) return null;

        httpClient ??= NetworkService.GetClient();
        cacheDirectory ??= Path.Combine(Paths.PluginTrust, "market-cache", "readme");
        Directory.CreateDirectory(cacheDirectory);
        var cachePath = Path.Combine(cacheDirectory,
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(location))) + ".md");

        foreach (var candidate in GitHubAccelerator.GetRequestCandidatesByConfig(location))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                using var request = new HttpRequestMessage(HttpMethod.Get, candidate);
                request.Headers.TryAddWithoutValidation("User-Agent", $"PCL-Nex/{Basics.VersionName}");
                request.Headers.TryAddWithoutValidation("Accept",
                    string.Equals(uri.Host, "api.github.com", StringComparison.OrdinalIgnoreCase)
                        ? "application/vnd.github.raw+json"
                        : "text/markdown, text/plain, */*");
                if (GitHubAccelerator.ShouldRewrite(location))
                {
                    request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", GitHubApiVersion);
                    var token = githubToken;
                    if (token is null)
                    {
                        try { token = Config.Plugin.GitHubToken; }
                        catch { }
                    }
                    if (!string.IsNullOrWhiteSpace(token))
                        request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token.Trim());
                }
                using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                    .ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) continue;
                var markdown = await ReadLimitedTextAsync(response, maxBytes, timeout.Token).ConfigureAwait(false);
                markdown = DecodeGitHubReadmeJson(markdown) ?? markdown;
                WriteTextCache(cachePath, markdown);
                return markdown;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch { }
        }

        return ReadTextCache(cachePath, maxBytes);
    }

    private static async Task<HttpResponseMessage> SendGitHubAsync(
        string url,
        string accept,
        PluginMarketQueryOptions options,
        HttpClient httpClient,
        CancellationToken ct)
    {
        Exception? lastError = null;
        var candidates = options.GitHubMirror.HasValue
            ? GitHubAccelerator.GetRequestCandidates(url, options.GitHubMirror.Value)
            : GitHubAccelerator.GetRequestCandidatesByConfig(url);
        for (var index = 0; index < candidates.Count; index++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, candidates[index]);
            request.Headers.TryAddWithoutValidation("User-Agent", $"PCL-Nex/{Basics.VersionName}");
            request.Headers.TryAddWithoutValidation("Accept", accept);
            request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", GitHubApiVersion);
            if (!string.IsNullOrWhiteSpace(options.GitHubToken))
                request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + options.GitHubToken.Trim());

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(options.RequestTimeout);
            try
            {
                var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                    .ConfigureAwait(false);
                if (response.IsSuccessStatusCode || index == candidates.Count - 1) return response;
                response.Dispose();
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastError = ex;
                if (index == candidates.Count - 1) throw;
            }
        }

        throw lastError ?? new HttpRequestException("GitHub request failed.");
    }

    private static void ThrowIfRateLimited(HttpResponseMessage response)
    {
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
            throw new GitHubRateLimitException(ReadRateLimitReset(response));
        if (response.StatusCode != HttpStatusCode.Forbidden) return;
        var remaining = response.Headers.TryGetValues("X-RateLimit-Remaining", out var values)
            ? values.FirstOrDefault()
            : null;
        if (!string.Equals(remaining, "0", StringComparison.Ordinal)) return;
        throw new GitHubRateLimitException(ReadRateLimitReset(response));
    }

    private static DateTimeOffset? ReadRateLimitReset(HttpResponseMessage response)
    {
        if (response.Headers.TryGetValues("X-RateLimit-Reset", out var resetValues)
            && long.TryParse(resetValues.FirstOrDefault(), out var seconds))
            return DateTimeOffset.FromUnixTimeSeconds(seconds);
        return null;
    }

    private static string BuildManifestApiUrl(GitHubRepository repository)
        => BuildRepositoryFileApiUrl(repository, "manifest.json");

    private static string BuildRepositoryFileApiUrl(GitHubRepository repository, string relativePath)
    {
        var owner = Uri.EscapeDataString(repository.Owner.Login);
        var name = Uri.EscapeDataString(repository.Name);
        var path = string.Join("/", relativePath.Split('/').Select(Uri.EscapeDataString));
        var branch = Uri.EscapeDataString(repository.DefaultBranch);
        return $"https://api.github.com/repos/{owner}/{name}/contents/{path}?ref={branch}";
    }

    private static string BuildManifestCommitsApiUrl(GitHubRepository repository)
    {
        var owner = Uri.EscapeDataString(repository.Owner.Login);
        var name = Uri.EscapeDataString(repository.Name);
        var branch = Uri.EscapeDataString(repository.DefaultBranch);
        return $"https://api.github.com/repos/{owner}/{name}/commits?path=manifest.json&sha={branch}&per_page=1";
    }

    private static string BuildReleasesApiUrl(GitHubRepository repository, int page)
    {
        var owner = Uri.EscapeDataString(repository.Owner.Login);
        var name = Uri.EscapeDataString(repository.Name);
        return $"https://api.github.com/repos/{owner}/{name}/releases?per_page=100&page={page}";
    }

    private static string BuildRawManifestUrl(GitHubRepository repository)
    {
        var owner = Uri.EscapeDataString(repository.Owner.Login);
        var name = Uri.EscapeDataString(repository.Name);
        var branch = string.Join("/", repository.DefaultBranch.Split('/').Select(Uri.EscapeDataString));
        return $"https://raw.githubusercontent.com/{owner}/{name}/refs/heads/{branch}/manifest.json";
    }

    private static async Task<string> ReadLimitedStringAsync(HttpResponseMessage response, int maxBytes, CancellationToken ct)
    {
        if (response.Content.Headers.ContentLength is > 0 and var length && length > maxBytes)
            throw new InvalidDataException($"manifest.json exceeds the {maxBytes} byte size limit.");

        await using var input = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var output = new MemoryStream();
        var buffer = new byte[16 * 1024];
        int read;
        while ((read = await input.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            if (output.Length + read > maxBytes)
                throw new InvalidDataException($"manifest.json exceeds the {maxBytes} byte size limit.");
            output.Write(buffer, 0, read);
        }
        return Encoding.UTF8.GetString(output.ToArray());
    }

    private static SemVer? ParsePluginVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var text = value.Trim();
        if (!string.Equals(text, value, StringComparison.Ordinal)
            || text.StartsWith("v", StringComparison.OrdinalIgnoreCase)) return null;
        return SemVer.TryParse(text, out var version) ? version : null;
    }

    private static bool IsPackageUrl(string? value)
        => !string.IsNullOrWhiteSpace(value)
           && Uri.TryCreate(value, UriKind.Absolute, out var uri)
           && string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
           && string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase)
           && uri.AbsolutePath.EndsWith(".pclx", StringComparison.OrdinalIgnoreCase);

    private static bool IsGeneralPackageUrl(string? value)
        => !string.IsNullOrWhiteSpace(value)
           && Uri.TryCreate(value, UriKind.Absolute, out var uri)
           && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
           && (uri.AbsolutePath.EndsWith(".pclx", StringComparison.OrdinalIgnoreCase)
               || uri.AbsolutePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));

    private static bool IsAbsoluteHttpUri(string? value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri)
           && (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
               || string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase));

    private static bool IsGitRepositoryUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (PluginRemoteInstallService.IsGitSource(value)) return true;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase)) return false;
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length == 2;
    }

    private static void ValidateOptions(PluginMarketQueryOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Topic)) throw new ArgumentException("GitHub topic cannot be empty.");
        if (options.PerPage is <= 0 or > 100) throw new ArgumentOutOfRangeException(nameof(options.PerPage));
        if (options.MaxPages <= 0) throw new ArgumentOutOfRangeException(nameof(options.MaxPages));
        if (options.MaxManifestBytes <= 0) throw new ArgumentOutOfRangeException(nameof(options.MaxManifestBytes));
        if (options.RequestTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(options.RequestTimeout));
    }

    private static string? ReadTextCache(string path, int maxBytes)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists && info.Length <= maxBytes ? File.ReadAllText(path) : null;
        }
        catch { return null; }
    }

    private static void WriteTextCache(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, content);
        File.Move(temp, path, true);
    }

    private static async Task<string> ReadLimitedTextAsync(HttpResponseMessage response, int maxBytes, CancellationToken ct)
    {
        if (response.Content.Headers.ContentLength is > 0 and var length && length > maxBytes)
            throw new InvalidDataException(Lang.Text("Plugins.Repository.Error.ReadmeSizeLimitExceeded"));
        await using var input = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var output = new MemoryStream();
        var buffer = new byte[16 * 1024];
        int read;
        while ((read = await input.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            if (output.Length + read > maxBytes) throw new InvalidDataException(Lang.Text("Plugins.Repository.Error.ReadmeSizeLimitExceeded"));
            output.Write(buffer, 0, read);
        }
        return Encoding.UTF8.GetString(output.ToArray());
    }

    private static string? DecodeGitHubReadmeJson(string value)
    {
        try
        {
            using var document = JsonDocument.Parse(value);
            var root = document.RootElement;
            if (!root.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.String) return null;
            var encoded = content.GetString()?.Replace("\r", "").Replace("\n", "");
            return string.IsNullOrWhiteSpace(encoded) ? null : Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
        }
        catch { return null; }
    }

    private sealed class GitHubRepositorySearchResult
    {
        [JsonPropertyName("total_count")]
        public int TotalCount { get; set; }

        [JsonPropertyName("items")]
        public List<GitHubRepository> Items { get; set; } = [];
    }

    private sealed class GitHubRepository
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("full_name")]
        public string FullName { get; set; } = string.Empty;

        [JsonPropertyName("html_url")]
        public string HtmlUrl { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("topics")]
        public List<string> Topics { get; set; } = [];

        [JsonPropertyName("default_branch")]
        public string DefaultBranch { get; set; } = "main";

        [JsonPropertyName("archived")]
        public bool Archived { get; set; }

        [JsonPropertyName("disabled")]
        public bool Disabled { get; set; }

        [JsonPropertyName("fork")]
        public bool Fork { get; set; }

        [JsonPropertyName("owner")]
        public GitHubOwner Owner { get; set; } = new();
    }

    private sealed class PluginRepositoryStatistics
    {
        [JsonPropertyName("manifestUpdatedAt")]
        public DateTimeOffset? ManifestUpdatedAt { get; set; }

        [JsonPropertyName("downloadCount")]
        public long DownloadCount { get; set; }
    }

    private sealed class GitHubCommitSummary
    {
        [JsonPropertyName("commit")]
        public GitHubCommitDetails? Commit { get; set; }
    }

    private sealed class GitHubCommitDetails
    {
        [JsonPropertyName("author")]
        public GitHubCommitIdentity? Author { get; set; }

        [JsonPropertyName("committer")]
        public GitHubCommitIdentity? Committer { get; set; }
    }

    private sealed class GitHubCommitIdentity
    {
        [JsonPropertyName("date")]
        public DateTimeOffset? Date { get; set; }
    }

    private sealed class GitHubReleaseSummary
    {
        [JsonPropertyName("assets")]
        public List<GitHubReleaseAssetSummary> Assets { get; set; } = [];
    }

    private sealed class GitHubReleaseAssetSummary
    {
        [JsonPropertyName("download_count")]
        public long DownloadCount { get; set; }
    }

    private sealed class LegacyRepositoryPluginMetadata
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("author")]
        public JsonElement? Author { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("logo")]
        public string? Logo { get; set; }
    }

    private sealed class GitHubOwner
    {
        [JsonPropertyName("login")]
        public string Login { get; set; } = string.Empty;

        [JsonPropertyName("avatar_url")]
        public string? AvatarUrl { get; set; }
    }

    private static List<string> NormalizeTags(IEnumerable<string> tags)
        => tags.Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Select(tag => tag.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(32)
            .ToList();

    internal static string? ResolveLogoUrl(string? logo, string sourceUrl, string? fallback)
        => ResolveResourceUrl(logo, sourceUrl, fallback);

    internal static string? ResolveResourceUrl(string? value, string sourceUrl, string? fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        value = value.Trim();
        if (IsAbsoluteHttpUri(value)) return value;
        if (Uri.TryCreate(sourceUrl, UriKind.Absolute, out var sourceUri)
            && (sourceUri.Scheme == Uri.UriSchemeHttp || sourceUri.Scheme == Uri.UriSchemeHttps))
            return new Uri(sourceUri, value).ToString();
        try
        {
            var baseDirectory = File.Exists(sourceUrl) ? Path.GetDirectoryName(Path.GetFullPath(sourceUrl)) : null;
            if (baseDirectory is null) return fallback;
            var candidate = Path.GetFullPath(Path.Combine(baseDirectory, value));
            return FileSystemPath.IsWithinDirectory(candidate, baseDirectory)
                ? candidate
                : fallback;
        }
        catch { return fallback; }
    }

    private static string BuildGitHubReadmeApiUrl(string fullName)
        => "https://api.github.com/repos/" + string.Join("/", fullName.Split('/').Select(Uri.EscapeDataString)) + "/readme";

    private static string GetSourceLabel(string sourceUrl)
    {
        if (Uri.TryCreate(sourceUrl, UriKind.Absolute, out var uri)) return uri.Host;
        try { return Path.GetFileName(sourceUrl); }
        catch { return "Source"; }
    }
}

public sealed class GitHubRateLimitException(DateTimeOffset? reset)
    : HttpRequestException(reset is null
        ? "GitHub API rate limit exceeded."
        : $"GitHub API rate limit exceeded until {reset:O}.")
{
    public DateTimeOffset? Reset { get; } = reset;
}
