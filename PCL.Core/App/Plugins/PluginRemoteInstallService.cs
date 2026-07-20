using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PCL.Core.App;
using PCL.Core.IO.Net.Http;
using PCL.Core.Logging;
using PCL.Core.Utils;

namespace PCL.Core.App.Plugins;

/// <summary>
/// 解析并准备来自远程 manifest、插件包或 Git 的插件安装源。
/// </summary>
public static class PluginRemoteInstallService
{
    private const int MaxManifestBytes = PluginMarketQueryOptions.DefaultManifestSizeLimit;

    public static async Task<PluginPreparedInstall> PrepareAsync(PluginInstallSourceEntry source, CancellationToken ct = default)
    {
        if (source is null || string.IsNullOrWhiteSpace(source.Url))
            throw new ArgumentException("安装来源不能为空。", nameof(source));

        var type = source.Type.Trim().ToLowerInvariant();
        return type switch
        {
            "package" => await PrepareRepositoryPackageAsync(source.Url, source.Sha256, ct).ConfigureAwait(false),
            "manifest" => await PrepareManifestAsync(source.Url, ct).ConfigureAwait(false),
            "git" => await PrepareGitAsync(source.Url, source.Ref, ct).ConfigureAwait(false),
            _ => throw new NotSupportedException("仅支持 manifest、.pclx 或 .zip 插件安装源。")
        };
    }

    public static Task<PluginPreparedInstall> PrepareGitRepositoryAsync(string source, CancellationToken ct = default)
        => PrepareGitAsync(source, null, ct);

    public static Task<PluginPreparedInstall> PrepareAsync(string source, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(source))
            throw new ArgumentException("安装来源不能为空。", nameof(source));

        source = source.Trim();
        if (LooksLikePackageUrl(source)) return PreparePackageAsync(source, null, ct);
        if (LooksLikeManifestUrl(source)) return PrepareManifestAsync(source, ct);
        return PrepareGitAsync(source, null, ct);
    }

    public static async Task<PluginPreparedInstall> PrepareManifestAsync(string manifestUrl, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(manifestUrl))
            throw new ArgumentException("插件 manifest 地址不能为空。", nameof(manifestUrl));

        var manifest = await FetchManifestAsync(manifestUrl, ct).ConfigureAwait(false)
            ?? throw new InvalidDataException("插件 manifest 解析失败。");
        var version = SelectCompatibleManifestVersion(manifest);

        return await PrepareManifestVersionAsync(manifestUrl, version, ct).ConfigureAwait(false);
    }

    public static async Task<PluginPreparedInstall> PrepareManifestVersionAsync(string manifestUrl, PluginMarketVersion version, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(manifestUrl))
            throw new ArgumentException("插件 manifest 地址不能为空。", nameof(manifestUrl));
        if (version is null)
            throw new ArgumentNullException(nameof(version));
        if (!LooksLikePackageUrl(version.ResolvedPackageUrl))
            throw new InvalidDataException("插件 manifest 版本缺少指向 .pclx 或 .zip 的 packageUrl。");
        if (!PluginRepositoryService.IsValidSha256(version.ResolvedSha256))
            throw new InvalidDataException("插件 manifest 版本缺少有效的 64 位十六进制 SHA-256。");

        var prepared = await PreparePackageAsync(
            version.ResolvedPackageUrl,
            version.ResolvedSha256,
            ct,
            version.PluginId,
            version.Version,
            version.ResolvedDependencies).ConfigureAwait(false);
        var sourceLabel = string.IsNullOrWhiteSpace(version.Version) ? "市场 manifest" : "市场 manifest（v" + version.Version + "）";
        return new PluginPreparedInstall(prepared.PluginRoot, prepared.Manifest, PluginInstallSourceType.Manifest,
            manifestUrl, sourceLabel, prepared.CleanupPath, prepared.VerifiedSha256);
    }

    /// <summary>
    /// 仅获取 manifest（不下载插件包），用于检查更新等轻量场景。<br/>
    /// 仅在用户启用 GitHub 加速时尝试已配置镜像，并始终以原始 URL 作为回退。
    /// </summary>
    public static async Task<PluginMarketManifest?> FetchManifestAsync(string manifestUrl, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(manifestUrl)) return null;

        var candidates = GetManifestFetchCandidates(manifestUrl);

        foreach (var (url, label) in candidates)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(8));
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                ApplyGitHubHeaders(request, manifestUrl, Config.Plugin.GitHubToken, "application/vnd.github.raw+json");
                using var resp = await request.SendAsync(retryTimes: 1, cancellationToken: timeoutCts.Token)
                    .ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode) continue;
                var manifest = await ReadManifestAsync(resp, timeoutCts.Token).ConfigureAwait(false);
                if (manifest is not null)
                {
                    PluginRepositoryService.ValidateMarketManifest(manifest);
                    if (!string.Equals(url, manifestUrl, StringComparison.OrdinalIgnoreCase))
                        LogWrapper.Debug("Plugin", "Manifest fetched via " + label + ": " + manifestUrl);
                    return manifest;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch { /* try next candidate */ }
        }

        LogWrapper.Debug("Plugin", "Manifest fetch exhausted all candidates: " + manifestUrl);
        return null;
    }

    internal static IReadOnlyList<(string Url, string Label)> GetManifestFetchCandidates(
        string manifestUrl,
        int? configuredMirror = null)
    {
        var candidates = new List<(string Url, string Label)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in configuredMirror.HasValue
                     ? GitHubAccelerator.GetRequestCandidates(manifestUrl, configuredMirror.Value)
                     : GitHubAccelerator.GetRequestCandidatesByConfig(manifestUrl))
        {
            if (seen.Add(candidate))
                candidates.Add((candidate, string.Equals(candidate, manifestUrl, StringComparison.OrdinalIgnoreCase)
                    ? "original"
                    : "configured"));
        }

        return candidates;
    }

    public static PluginMarketVersion SelectCompatibleManifestVersion(PluginMarketManifest manifest, string? currentHostVersion = null)
    {
        if (manifest is null) throw new ArgumentNullException(nameof(manifest));
        PluginRepositoryService.ValidateMarketManifest(manifest);

        var candidates = (manifest.Versions ?? [])
            .Select(version => new
            {
                Version = version,
                Download = PluginRepositoryService.SelectDownload(version, RuntimeInformation.OSArchitecture)
            })
            .Where(item => item.Download is not null)
            .Select((version, index) => new
            {
                Version = version.Version,
                Download = version.Download!,
                Index = index,
                ParsedVersion = TryParsePackageVersion(version.Version.Version)
            })
            .ToList();

        if (candidates.Count == 0)
            throw new InvalidDataException("插件 manifest 缺少指向 .pclx 或 .zip 的 versions 条目。");

        currentHostVersion ??= PluginCompatibility.CurrentPclCoreVersion;
        var selected = candidates
            .Where(candidate => IsManifestVersionCompatible(candidate.Version, currentHostVersion))
            .OrderByDescending(candidate => candidate.ParsedVersion is not null)
            .ThenByDescending(candidate => candidate.ParsedVersion)
            .ThenBy(candidate => candidate.Index)
            .FirstOrDefault();

        if (selected is null)
            throw new InvalidDataException("插件 manifest 中没有当前平台可安装且未过旧的版本。");

        selected.Version.ResolvedPackageUrl = selected.Download.PackageUrl;
        selected.Version.ResolvedSha256 = selected.Download.Sha256;
        return selected.Version;
    }

    public static async Task<PluginPreparedInstall> PreparePackageAsync(
        string packageUrl,
        string? expectedSha256 = null,
        CancellationToken ct = default,
        string? expectedPluginId = null,
        string? expectedVersion = null,
        IReadOnlyList<PluginDependency>? expectedDependencies = null)
    {
        if (string.IsNullOrWhiteSpace(packageUrl))
            throw new ArgumentException("插件包地址不能为空。", nameof(packageUrl));
        if (!LooksLikePackageUrl(packageUrl))
            throw new NotSupportedException("插件包必须是 .pclx 或 .zip 文件。");
        if (!Uri.TryCreate(packageUrl, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            throw new NotSupportedException("插件包地址必须是 HTTP 或 HTTPS。");

        var extension = Path.GetExtension(uri.AbsolutePath);
        if (string.IsNullOrWhiteSpace(extension)) extension = ".zip";
        var workDir = Path.Combine(PCL.Core.App.Paths.PluginTemp, "remote_" + Guid.NewGuid().ToString("N"));
        var packagePath = Path.Combine(workDir, "package" + extension);
        var extractDir = Path.Combine(workDir, "extract");
        Directory.CreateDirectory(extractDir);

        try
        {
            HttpResponseMessage? response = null;
            try
            {
                Exception? lastDownloadError = null;
                var downloadCandidates = GitHubAccelerator.GetRequestCandidatesByConfig(packageUrl);
                for (var index = 0; index < downloadCandidates.Count; index++)
                {
                    try
                    {
                        using var request = new HttpRequestMessage(HttpMethod.Get, downloadCandidates[index]);
                        ApplyGitHubHeaders(request, packageUrl, Config.Plugin.GitHubToken);
                        response = await request.SendAsync(
                                httpCompletionOption: HttpCompletionOption.ResponseHeadersRead,
                                cancellationToken: ct)
                            .ConfigureAwait(false);
                        if (response.IsSuccessStatusCode || index == downloadCandidates.Count - 1) break;
                        response.Dispose();
                        response = null;
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                    catch (Exception ex)
                    {
                        lastDownloadError = ex;
                        if (index == downloadCandidates.Count - 1) throw;
                    }
                }

                if (response is null) throw lastDownloadError ?? new HttpRequestException("插件包下载失败。");
                response.EnsureSuccessStatusCode();
                await using var remoteStream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                await using var fileStream = File.Create(packagePath);
                await remoteStream.CopyToAsync(fileStream, ct).ConfigureAwait(false);
            }
            finally
            {
                response?.Dispose();
            }

            var verifiedSha256 = ValidateSha256(packagePath, expectedSha256);
            await Task.Run(() => ExtractZipSafely(packagePath, extractDir), ct).ConfigureAwait(false);
            var pluginRoot = FindPluginRoot(extractDir)
                ?? throw new InvalidDataException("插件包中未找到 plugin.json。请确认该文件是 PCL 插件包。");
            var (manifest, result) = await PluginPackageService.ReadAndValidateDirectoryAsync(pluginRoot, ct).ConfigureAwait(false);
            if (!result.IsValid || manifest is null)
                throw new InvalidDataException(result.ErrorMessage ?? "插件包校验失败。");
            ValidateSelectedMarketIdentity(manifest, expectedPluginId, expectedVersion, expectedDependencies);

            return new PluginPreparedInstall(pluginRoot, manifest, PluginInstallSourceType.Repository,
                packageUrl, "远程插件包", workDir, verifiedSha256);
        }
        catch
        {
            try { if (Directory.Exists(workDir)) Directory.Delete(workDir, recursive: true); }
            catch { }
            throw;
        }
    }

    public static bool IsGitSource(string source)
    {
        return source.StartsWith("git+", StringComparison.OrdinalIgnoreCase)
            || source.EndsWith(".git", StringComparison.OrdinalIgnoreCase)
            || source.StartsWith("git@", StringComparison.OrdinalIgnoreCase);
    }

    public static bool LooksLikePackageUrl(string source)
    {
        if (string.IsNullOrWhiteSpace(source)) return false;
        var path = Uri.TryCreate(source, UriKind.Absolute, out var uri) ? uri.AbsolutePath : source;
        return path.EndsWith(".pclx", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
    }

    public static bool LooksLikeManifestUrl(string source)
    {
        if (string.IsNullOrWhiteSpace(source)) return false;
        var path = Uri.TryCreate(source, UriKind.Absolute, out var uri) ? uri.AbsolutePath : source;
        return path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            && !LooksLikePackageUrl(source);
    }

    private static bool IsManifestVersionCompatible(PluginMarketVersion version, string? currentHostVersion)
    {
        return PluginCompatibility.EvaluatePclCoreVersion(version.PclCoreVersion, currentHostVersion)
            != PluginCoreCompatibilityStatus.TooOld;
    }

    private static Task<PluginPreparedInstall> PrepareRepositoryPackageAsync(string packageUrl, string? expectedSha256, CancellationToken ct)
    {
        if (!PluginRepositoryService.IsValidSha256(expectedSha256))
            throw new InvalidDataException("市场插件包必须提供有效的 64 位十六进制 SHA-256。");
        return PreparePackageAsync(packageUrl, expectedSha256, ct);
    }

    internal static async Task<PluginMarketManifest?> ReadManifestAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.Content.Headers.ContentLength is > MaxManifestBytes)
            throw new InvalidDataException($"manifest.json exceeds the {MaxManifestBytes} byte size limit.");

        await using var input = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var output = new MemoryStream();
        var buffer = new byte[16 * 1024];
        int read;
        while ((read = await input.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            if (output.Length + read > MaxManifestBytes)
                throw new InvalidDataException($"manifest.json exceeds the {MaxManifestBytes} byte size limit.");
            output.Write(buffer, 0, read);
        }

        try
        {
            return JsonSerializer.Deserialize<PluginMarketManifest>(
                Encoding.UTF8.GetString(output.ToArray()),
                PluginJson.SerializerOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("manifest.json contains invalid JSON.", ex);
        }
    }

    internal static void ValidateSelectedMarketIdentity(
        PluginPackageManifest packageManifest,
        string? expectedPluginId,
        string? expectedVersion,
        IReadOnlyList<PluginDependency>? expectedDependencies = null)
    {
        if (!string.IsNullOrWhiteSpace(expectedPluginId)
            && !string.Equals(packageManifest.Id, expectedPluginId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                $"插件包 ID {packageManifest.Id} 与市场 manifest 的 {expectedPluginId} 不一致。");

        if (!string.IsNullOrWhiteSpace(expectedVersion))
        {
            var expected = TryParsePackageVersion(expectedVersion)
                ?? throw new InvalidDataException("市场 manifest 包含无效的插件版本。");
            var actual = TryParsePackageVersion(packageManifest.Version)
                ?? throw new InvalidDataException("插件包包含无效的 SemVer 版本。");
            if (actual != expected)
                throw new InvalidDataException(
                    $"插件包版本 {packageManifest.Version} 与市场 manifest 的 {expectedVersion} 不一致。");
        }

        if (expectedDependencies is not null
            && !PluginDependencyService.DependencyListsEqual(packageManifest.Dependencies, expectedDependencies))
            throw new InvalidDataException("插件包 dependencies 与市场 manifest 声明不一致。");
    }

    internal static void ApplyGitHubHeaders(
        HttpRequestMessage request,
        string originalUrl,
        string? token,
        string? accept = null)
    {
        // GitHub authentication/API headers are determined by the original trusted GitHub host,
        // not by whether the user selected that host for mirror acceleration.
        if (!GitHubAccelerator.ShouldRewrite(originalUrl)) return;
        request.Headers.TryAddWithoutValidation("User-Agent", $"PCL-Nex/{Basics.VersionName}");
        if (!string.IsNullOrWhiteSpace(accept)) request.Headers.TryAddWithoutValidation("Accept", accept);
        request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
        if (!string.IsNullOrWhiteSpace(token))
            request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token.Trim());
    }

    private static SemVer? TryParsePackageVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var text = value.Trim();
        if (text.StartsWith("v", StringComparison.OrdinalIgnoreCase)) text = text[1..];
        return SemVer.TryParse(text, out var version) ? version : null;
    }

    public static PluginGitSource ParseGitSource(string source, string? reference = null)
    {
        if (string.IsNullOrWhiteSpace(source))
            throw new ArgumentException("安装来源不能为空。", nameof(source));

        var cloneUrl = source.Trim();
        if (cloneUrl.StartsWith("git+", StringComparison.OrdinalIgnoreCase)) cloneUrl = cloneUrl[4..];

        var gitRef = string.IsNullOrWhiteSpace(reference) ? null : reference.Trim();
        string? inlineRef = null;

        var fragmentIndex = cloneUrl.LastIndexOf('#');
        if (fragmentIndex >= 0)
        {
            inlineRef = cloneUrl[(fragmentIndex + 1)..].Trim();
            cloneUrl = cloneUrl[..fragmentIndex];
        }

        var gitSuffixIndex = cloneUrl.LastIndexOf(".git@", StringComparison.OrdinalIgnoreCase);
        if (gitSuffixIndex >= 0)
        {
            var refStart = gitSuffixIndex + ".git@".Length;
            inlineRef = cloneUrl[refStart..].Trim();
            cloneUrl = cloneUrl[..(gitSuffixIndex + ".git".Length)];
        }

        if (string.IsNullOrWhiteSpace(gitRef)) gitRef = inlineRef;

        if (string.IsNullOrWhiteSpace(cloneUrl))
            throw new ArgumentException("Git 仓库地址不能为空。", nameof(source));

        return new PluginGitSource(cloneUrl, string.IsNullOrWhiteSpace(gitRef) ? null : gitRef);
    }

    public static string FormatGitSourceUrl(string source, string? reference = null)
    {
        var gitSource = ParseGitSource(source, reference);
        return gitSource.ToDisplayString();
    }

    private static async Task<PluginPreparedInstall> PrepareGitAsync(string source, string? reference, CancellationToken ct)
    {
        var gitSource = ParseGitSource(source, reference);
        var cloneUrl = RewriteGitCloneUrl(gitSource.CloneUrl, Config.Download.PluginGitMirror);
        var workDir = Path.Combine(PCL.Core.App.Paths.PluginTemp, "git_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDir);

        await RunGitAsync(BuildCloneArguments(cloneUrl, gitSource.Reference, workDir), ct).ConfigureAwait(false);

        var pluginRoot = FindPluginRoot(workDir)
            ?? throw new InvalidDataException("Git 仓库中未找到 plugin.json。请确认该仓库根目录或子目录包含 PCL 插件。" );
        var (manifest, result) = await PluginPackageService.ReadAndValidateDirectoryAsync(pluginRoot, ct).ConfigureAwait(false);
        if (!result.IsValid || manifest is null)
            throw new InvalidDataException(result.ErrorMessage ?? "插件目录校验失败。");

        var sourceLabel = string.IsNullOrWhiteSpace(gitSource.Reference) ? "Git 安装" : "Git 安装（" + gitSource.Reference + "）";
        return new PluginPreparedInstall(pluginRoot, manifest, PluginInstallSourceType.Git, gitSource.ToDisplayString(), sourceLabel, workDir);
    }

    private static string? FindPluginRoot(string root)
    {
        var direct = Path.Combine(root, "plugin.json");
        if (File.Exists(direct)) return root;

        return Directory.GetFiles(root, "plugin.json", SearchOption.AllDirectories)
            .Where(path => !path.Contains(Path.DirectorySeparatorChar + "node_modules" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path.Length)
            .Select(Path.GetDirectoryName)
            .FirstOrDefault(path => !string.IsNullOrWhiteSpace(path));
    }

    internal static void ExtractZipSafely(string archivePath, string destinationDirectory)
    {
        var destinationRoot = Path.GetFullPath(destinationDirectory) + Path.DirectorySeparatorChar;
        using var archive = ZipFile.OpenRead(archivePath);
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.FullName)) continue;
            var targetPath = Path.GetFullPath(Path.Combine(destinationRoot, entry.FullName));
            if (!targetPath.StartsWith(destinationRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("插件包包含不安全的路径。");

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(targetPath);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            entry.ExtractToFile(targetPath, overwrite: true);
        }
    }

    internal static string ValidateSha256(string filePath, string? expectedSha256)
    {
        if (!string.IsNullOrWhiteSpace(expectedSha256) && !PluginRepositoryService.IsValidSha256(expectedSha256))
            throw new InvalidDataException("插件包 SHA-256 格式无效。");

        using var stream = File.OpenRead(filePath);
        var actual = Convert.ToHexString(SHA256.HashData(stream));
        if (string.IsNullOrWhiteSpace(expectedSha256)) return actual;
        var expected = expectedSha256.Trim().Replace(" ", string.Empty).Replace("-", string.Empty);
        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("插件包 SHA-256 校验失败。");
        return actual;
    }

    public static string RewriteGitCloneUrl(string cloneUrl, int mirror)
    {
        return GitHubAccelerator.Rewrite(cloneUrl, mirror, GitHubAccelerator.GetConfiguredDomains());
    }

    private static async Task RunGitAsync(string arguments, CancellationToken ct)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            }
        };

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("无法启动 git。请确认已安装 Git 并加入 PATH。", ex);
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct).ConfigureAwait(false);
        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);

        if (process.ExitCode != 0)
            throw new InvalidOperationException("Git 克隆失败：" + (string.IsNullOrWhiteSpace(stderr) ? stdout : stderr));
    }

    private static string QuoteArg(string value)
    {
        return "\"" + value.Replace("\"", "\\\"") + "\"";
    }

    private static string BuildCloneArguments(string cloneUrl, string? reference, string workDir)
    {
        var args = "clone --depth 1 ";
        if (!string.IsNullOrWhiteSpace(reference)) args += "--branch " + QuoteArg(reference) + " ";
        return args + QuoteArg(cloneUrl) + " " + QuoteArg(workDir);
    }

}

public sealed record PluginGitSource(string CloneUrl, string? Reference)
{
    public string ToDisplayString() => string.IsNullOrWhiteSpace(Reference) ? CloneUrl : CloneUrl + "#" + Reference;
}

public sealed class PluginPreparedInstall : IDisposable
{
    public PluginPreparedInstall(
        string pluginRoot,
        PluginPackageManifest manifest,
        PluginInstallSourceType sourceType,
        string sourceUrl,
        string sourceLabel,
        string cleanupPath,
        string? verifiedSha256 = null)
    {
        PluginRoot = pluginRoot;
        Manifest = manifest;
        SourceType = sourceType;
        SourceUrl = sourceUrl;
        SourceLabel = sourceLabel;
        CleanupPath = cleanupPath;
        VerifiedSha256 = verifiedSha256;
    }

    public string PluginRoot { get; }

    public PluginPackageManifest Manifest { get; }

    public PluginInstallSourceType SourceType { get; }

    public string SourceUrl { get; }

    public string SourceLabel { get; }

    public string CleanupPath { get; }

    /// <summary>实际下载或导入包的 SHA-256；目录/Git 来源为空。</summary>
    public string? VerifiedSha256 { get; }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(CleanupPath)) Directory.Delete(CleanupPath, recursive: true);
            else if (File.Exists(CleanupPath)) File.Delete(CleanupPath);
        }
        catch { }
    }
}
