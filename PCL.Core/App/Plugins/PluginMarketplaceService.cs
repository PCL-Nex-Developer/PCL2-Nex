using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PCL.Core.App.Localization;
using PCL.Core.IO.Net;
using PCL.Core.IO.Net.Http;

namespace PCL.Core.App.Plugins;

/// <summary>
/// 汇总客户端内置 GitHub Topic、NexDeveloper market、额外 manifest、内联 plugins 与用户来源。
/// </summary>
public static class PluginMarketplaceService
{
    public static async Task<PluginMarketLoadResult> LoadAsync(
        PluginMarketQueryOptions? options = null,
        HttpClient? httpClient = null,
        CancellationToken ct = default)
    {
        options ??= new PluginMarketQueryOptions();
        httpClient ??= NetworkService.GetClient();
        var state = new LoadState();

        await LoadTopicAsync("pclnexplugin", "GitHub", [], options, httpClient, state, ct)
            .ConfigureAwait(false);

        await LoadOfficialSourceAsync(options, httpClient, state, ct).ConfigureAwait(false);

        foreach (var record in PluginTrustService.GetAllTrustRecords().Where(record => record.Enabled))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                switch (record.SourceKind)
                {
                    case PluginRepositorySourceKind.Topic:
                        // Topic discovery is owned by the client and is not a user-configurable source.
                        continue;
                    case PluginRepositorySourceKind.Manifest:
                        await LoadManifestAsync(record.RepoUrl, record.RepoName, [], options, state, ct)
                            .ConfigureAwait(false);
                        break;
                    default:
                        await LoadJsonSourceAsync(record.RepoUrl, record.RepoName, options, httpClient, state, ct)
                            .ConfigureAwait(false);
                        break;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                state.Errors.Add(new PluginMarketError(record.RepoName, ex.Message));
            }
        }

        foreach (var manifestUrl in (Config.Plugin.ManifestSubscriptions ?? [])
                     .Where(value => !string.IsNullOrWhiteSpace(value))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                await LoadJsonSourceAsync(manifestUrl.Trim(), string.Empty, options, httpClient, state, ct)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                state.Errors.Add(new PluginMarketError(manifestUrl, ex.Message));
            }
        }

        var entries = state.Entries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Id))
            .GroupBy(entry => $"{entry.Id}|{entry.SourceRepoUrl}|{entry.ManifestUrl}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
        return CreateResult(entries, state);
    }

    internal static async Task<PluginMarketLoadResult> LoadOfficialSourceForTestingAsync(
        PluginMarketQueryOptions? options = null,
        HttpClient? httpClient = null,
        CancellationToken ct = default)
    {
        options ??= new PluginMarketQueryOptions();
        httpClient ??= NetworkService.GetClient();
        var state = new LoadState();
        await LoadOfficialSourceAsync(options, httpClient, state, ct).ConfigureAwait(false);
        return CreateResult(state.Entries, state);
    }

    private static async Task LoadOfficialSourceAsync(
        PluginMarketQueryOptions options,
        HttpClient httpClient,
        LoadState state,
        CancellationToken ct)
    {
        try
        {
            await LoadJsonSourceAsync(
                PluginRepositoryService.OfficialMarketSourceUrl,
                "NexDeveloper",
                options,
                httpClient,
                state,
                ct,
                sourceIsOfficial: true).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            state.Errors.Add(new PluginMarketError("NexDeveloper", ex.Message));
        }
    }

    internal static async Task<PluginMarketLoadResult> LoadSourceAsync(
        string source,
        PluginMarketQueryOptions? options = null,
        HttpClient? httpClient = null,
        CancellationToken ct = default)
    {
        options ??= new PluginMarketQueryOptions();
        httpClient ??= NetworkService.GetClient();
        var state = new LoadState();
        await LoadJsonSourceAsync(source, string.Empty, options, httpClient, state, ct).ConfigureAwait(false);
        return CreateResult(state.Entries, state);
    }

    private static async Task LoadJsonSourceAsync(
        string location,
        string sourceName,
        PluginMarketQueryOptions options,
        HttpClient httpClient,
        LoadState state,
        CancellationToken ct,
        bool sourceIsOfficial = false)
    {
        var (json, usedCache) = await ReadSourceTextAsync(location, options, httpClient, ct).ConfigureAwait(false);
        state.UsedCache |= usedCache;
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        });
        var root = document.RootElement;

        if (root.TryGetProperty("id", out _) && root.TryGetProperty("versions", out _))
        {
            var manifest = JsonSerializer.Deserialize<PluginMarketManifest>(json, PluginJson.SerializerOptions)
                ?? throw new InvalidDataException(Text("Plugins.Marketplace.Error.ManifestEmpty", "插件 manifest 为空。"));
            var displayName = FirstNonEmpty(sourceName, manifest.Name, GetSourceLabel(location)) ?? manifest.Name;
            var entry = PluginRepositoryService.CreateManifestEntry(
                manifest, location, options.Architecture, "Manifest", displayName);
            entry.SourceIsOfficial = sourceIsOfficial;
            state.Entries.Add(entry);
            return;
        }

        if (root.TryGetProperty("topics", out _))
            throw new InvalidDataException(Text(
                "Plugins.Marketplace.Error.TopicsNotSupported",
                "plugin-market.json 不能声明 topics；GitHub Topic 由启动器内置维护。"));

        var isSourceDocument = root.TryGetProperty("manifests", out _)
                               || root.TryGetProperty("developers", out _)
                               || LooksLikeInlinePluginDocument(root);
        if (isSourceDocument)
        {
            var source = JsonSerializer.Deserialize<PluginMarketSourceDocument>(json, PluginJson.SerializerOptions)
                ?? throw new InvalidDataException(Text("Plugins.Marketplace.Error.SourceJsonEmpty", "插件来源 JSON 为空。"));
            if (source.Version != 1) throw new InvalidDataException(Text("Plugins.Marketplace.Error.UnsupportedSourceVersion", "不支持的插件来源 JSON 版本。"));
            var displayName = FirstNonEmpty(sourceName, source.Name, source.Group, GetSourceLabel(location))
                              ?? Text("Plugins.Marketplace.Label.CustomSource", "自定义来源");
            var inheritedTags = NormalizeTags(source.Tags);
            var developers = PluginDeveloperTrustService.NormalizeSourceDevelopers(
                source.Developers, sourceIsOfficial);
            if (sourceIsOfficial)
            {
                foreach (var developer in developers)
                    state.OfficialDevelopers.TryAdd(developer.GitHubLogin, developer);
            }
            else
            {
                foreach (var developer in developers)
                    state.TrustedDeveloperLogins.Add(developer.GitHubLogin);
            }

            foreach (var manifestLocation in source.Manifests.Where(value => !string.IsNullOrWhiteSpace(value)))
                await LoadManifestAsync(
                        ResolveLocation(location, manifestLocation.Trim()),
                        displayName,
                        inheritedTags,
                        options,
                        state,
                        ct)
                    .ConfigureAwait(false);

            foreach (var manifest in source.Plugins)
            {
                var entry = PluginRepositoryService.CreateManifestEntry(
                    manifest,
                    location,
                    options.Architecture,
                    "Json",
                    displayName,
                    inheritedTags);
                entry.SourceIsOfficial = sourceIsOfficial;
                state.Entries.Add(entry);
            }
            return;
        }

        var legacy = JsonSerializer.Deserialize<PluginRepositoryIndex>(json, PluginJson.SerializerOptions)
            ?? throw new InvalidDataException(Text("Plugins.Marketplace.Error.UnrecognizedJson", "插件 JSON 不是 developers/manifests/plugins 来源文档、manifest 或旧索引。"));
        PluginRepositoryService.NormalizeIndex(legacy, location);
        foreach (var entry in legacy.Plugins)
        {
            entry.SourceKind = "Json";
            entry.SourceGroup = FirstNonEmpty(sourceName, legacy.Name, GetSourceLabel(location)) ?? Text("Plugins.Marketplace.Label.CustomSource", "自定义来源");
            entry.Tags = NormalizeTags(entry.Tags);
            entry.Logo = PluginRepositoryService.ResolveLogoUrl(entry.Logo ?? entry.IconUrl, location, null);
            entry.SourceIsOfficial = sourceIsOfficial;
            state.Entries.Add(entry);
        }
    }

    private static async Task LoadTopicAsync(
        string topic,
        string group,
        IReadOnlyCollection<string> inheritedTags,
        PluginMarketQueryOptions options,
        HttpClient httpClient,
        LoadState state,
        CancellationToken ct)
    {
        var topicOptions = CloneOptions(options, topic);
        var result = await PluginRepositoryService.SearchTopicAsync(topicOptions, httpClient, ct).ConfigureAwait(false);
        foreach (var entry in result.Entries)
        {
            entry.SourceKind = "GitHub";
            entry.SourceGroup = group;
            entry.Tags = NormalizeTags(entry.Tags.Concat(inheritedTags));
            state.Entries.Add(entry);
        }
        state.Errors.AddRange(result.Errors);
        state.UsedCache |= result.UsedRepositoryCache;
        state.RateLimited |= result.RateLimited;
        state.RateLimitReset ??= result.RateLimitReset;
    }

    private static async Task LoadManifestAsync(
        string manifestUrl,
        string group,
        IReadOnlyCollection<string> inheritedTags,
        PluginMarketQueryOptions options,
        LoadState state,
        CancellationToken ct)
    {
        PluginMarketManifest? manifest;
        if (File.Exists(manifestUrl))
        {
            var info = new FileInfo(manifestUrl);
            if (info.Length > options.MaxManifestBytes) throw new InvalidDataException(Text("Plugins.Marketplace.Error.ManifestTooLarge", "插件 manifest 文件过大。"));
            manifest = JsonSerializer.Deserialize<PluginMarketManifest>(
                await File.ReadAllTextAsync(manifestUrl, ct).ConfigureAwait(false),
                PluginJson.SerializerOptions);
            if (manifest is not null) PluginRepositoryService.ValidateMarketManifest(manifest);
        }
        else
        {
            manifest = await PluginRemoteInstallService.FetchManifestAsync(manifestUrl, ct).ConfigureAwait(false);
        }
        if (manifest is null) throw new InvalidDataException(Text("Plugins.Marketplace.Error.ManifestLoadFailed", "插件 manifest 获取或解析失败。"));
        state.Entries.Add(PluginRepositoryService.CreateManifestEntry(
            manifest, manifestUrl, options.Architecture, "Manifest", group, inheritedTags));
    }

    private static async Task<(string Json, bool UsedCache)> ReadSourceTextAsync(
        string location,
        PluginMarketQueryOptions options,
        HttpClient httpClient,
        CancellationToken ct)
    {
        if (File.Exists(location))
        {
            var info = new FileInfo(location);
            if (info.Length > options.MaxManifestBytes) throw new InvalidDataException(Text("Plugins.Marketplace.Error.SourceJsonTooLarge", "插件来源 JSON 文件过大。"));
            return (await File.ReadAllTextAsync(location, ct).ConfigureAwait(false), false);
        }

        if (!Uri.TryCreate(location, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https"))
            throw new InvalidDataException(Text("Plugins.Marketplace.Error.InvalidSource", "插件来源必须是 HTTP/HTTPS JSON 地址或本地 JSON 文件。"));

        var cacheDirectory = options.CacheDirectory ?? Path.Combine(Paths.PluginTrust, "market-cache");
        var sourceCache = Path.Combine(cacheDirectory, "sources");
        Directory.CreateDirectory(sourceCache);
        var cachePath = Path.Combine(sourceCache,
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(location))) + ".json");

        Exception? lastError = null;
        foreach (var candidate in options.GitHubMirror.HasValue
                     ? GitHubAccelerator.GetRequestCandidates(location, options.GitHubMirror.Value)
                     : GitHubAccelerator.GetRequestCandidatesByConfig(location))
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, candidate);
            request.Headers.TryAddWithoutValidation("User-Agent", $"PCL-Nex/{Basics.VersionName}");
            request.Headers.TryAddWithoutValidation("Accept", "application/json");
            if (GitHubAccelerator.ShouldRewrite(location))
                request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
            if (GitHubAccelerator.ShouldRewrite(location) && !string.IsNullOrWhiteSpace(options.GitHubToken))
                request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + options.GitHubToken.Trim());
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(options.RequestTimeout);
            try
            {
                using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                    .ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) continue;
                var json = await ReadLimitedAsync(response, options.MaxManifestBytes, timeout.Token).ConfigureAwait(false);
                WriteCache(cachePath, json);
                return (json, false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) { lastError = ex; }
        }

        if (File.Exists(cachePath)) return (File.ReadAllText(cachePath), true);
        throw lastError ?? new HttpRequestException(Text("Plugins.Marketplace.Error.SourceJsonFetchFailed", "插件来源 JSON 获取失败。"));
    }

    private static async Task<string> ReadLimitedAsync(HttpResponseMessage response, int maxBytes, CancellationToken ct)
    {
        if (response.Content.Headers.ContentLength is > 0 and var length && length > maxBytes)
            throw new InvalidDataException(Text("Plugins.Marketplace.Error.SourceJsonSizeLimitExceeded", "插件来源 JSON 超过大小限制。"));
        await using var input = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var output = new MemoryStream();
        var buffer = new byte[16 * 1024];
        int read;
        while ((read = await input.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            if (output.Length + read > maxBytes) throw new InvalidDataException(Text("Plugins.Marketplace.Error.SourceJsonSizeLimitExceeded", "插件来源 JSON 超过大小限制。"));
            output.Write(buffer, 0, read);
        }
        return Encoding.UTF8.GetString(output.ToArray());
    }

    private static void WriteCache(string path, string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, json);
        File.Move(temp, path, true);
    }

    private static bool LooksLikeInlinePluginDocument(JsonElement root)
    {
        if (!root.TryGetProperty("plugins", out var plugins) || plugins.ValueKind != JsonValueKind.Array)
            return false;
        foreach (var plugin in plugins.EnumerateArray())
            if (plugin.ValueKind == JsonValueKind.Object && plugin.TryGetProperty("versions", out _)) return true;
        return false;
    }

    private static string ResolveLocation(string parent, string child)
    {
        if (Uri.TryCreate(child, UriKind.Absolute, out _)) return child;
        if (Uri.TryCreate(parent, UriKind.Absolute, out var parentUri)
            && parentUri.Scheme is "http" or "https") return new Uri(parentUri, child).ToString();
        var directory = File.Exists(parent) ? Path.GetDirectoryName(Path.GetFullPath(parent)) : null;
        return directory is null ? child : Path.GetFullPath(Path.Combine(directory, child));
    }

    private static PluginMarketQueryOptions CloneOptions(PluginMarketQueryOptions source, string topic) => new()
    {
        Topic = topic,
        GitHubToken = source.GitHubToken,
        PerPage = source.PerPage,
        MaxPages = source.MaxPages,
        MaxManifestBytes = source.MaxManifestBytes,
        RequestTimeout = source.RequestTimeout,
        CacheDirectory = source.CacheDirectory,
        GitHubMirror = source.GitHubMirror,
        Architecture = source.Architecture,
        IncludeArchived = source.IncludeArchived,
        IncludeDisabled = source.IncludeDisabled,
        IncludeForks = source.IncludeForks
    };

    private static List<string> NormalizeTags(IEnumerable<string> tags)
        => tags.Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Select(tag => tag.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(32)
            .ToList();

    private static string GetSourceLabel(string source)
    {
        if (Uri.TryCreate(source, UriKind.Absolute, out var uri)) return uri.Host;
        try { return Path.GetFileName(source); }
        catch { return Text("Plugins.Marketplace.Label.CustomSource", "自定义来源"); }
    }

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private static string Text(string key, string fallback, params object?[] args)
    {
        var template = Lang.Text(key);
        if (string.Equals(template, key, StringComparison.Ordinal)
            || string.Equals(template, $"!{key}!", StringComparison.Ordinal))
            template = fallback;
        return string.Format(Lang.Culture, template, args);
    }

    private static PluginMarketLoadResult CreateResult(
        IReadOnlyList<PluginRepositoryEntry> entries,
        LoadState state)
        => new(entries, state.Errors, state.UsedCache, state.RateLimited, state.RateLimitReset)
        {
            OfficialDevelopers = state.OfficialDevelopers.Values.ToArray(),
            TrustedDeveloperLogins = state.TrustedDeveloperLogins.ToArray()
        };

    private sealed class LoadState
    {
        public List<PluginRepositoryEntry> Entries { get; } = [];
        public List<PluginMarketError> Errors { get; } = [];
        public Dictionary<string, PluginDeveloperRecord> OfficialDevelopers { get; } =
            new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> TrustedDeveloperLogins { get; } = new(StringComparer.OrdinalIgnoreCase);
        public bool UsedCache { get; set; }
        public bool RateLimited { get; set; }
        public DateTimeOffset? RateLimitReset { get; set; }
    }
}
