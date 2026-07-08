using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using PCL.Core.App;
using PCL.Core.IO.Net.Http;
using PCL.Core.Logging;

namespace PCL.Core.App.Plugins;

/// <summary>
/// 解析并准备来自远程 manifest、插件包或 Git 的插件安装源。
/// </summary>
public static class PluginRemoteInstallService
{
    public static async Task<PluginPreparedInstall> PrepareAsync(PluginInstallSourceEntry source, CancellationToken ct = default)
    {
        if (source is null || string.IsNullOrWhiteSpace(source.Url))
            throw new ArgumentException("安装来源不能为空。", nameof(source));

        var type = source.Type.Trim().ToLowerInvariant();
        return type switch
        {
            "package" => await PreparePackageAsync(source.Url, source.Sha256, ct).ConfigureAwait(false),
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
        if (!LooksLikePackageUrl(version.PackageUrl))
            throw new InvalidDataException("插件 manifest 版本缺少指向 .pclx 或 .zip 的 packageUrl。");

        var prepared = await PreparePackageAsync(version.PackageUrl, version.Sha256, ct).ConfigureAwait(false);
        var sourceLabel = string.IsNullOrWhiteSpace(version.Version) ? "市场 manifest" : "市场 manifest（v" + version.Version + "）";
        return new PluginPreparedInstall(prepared.PluginRoot, prepared.Manifest, PluginInstallSourceType.Repository, manifestUrl, sourceLabel, prepared.CleanupPath);
    }

    /// <summary>
    /// 仅获取 manifest（不下载插件包），用于检查更新等轻量场景。<br/>
    /// 会依次尝试配置的镜像、所有 GitHub 加速镜像，确保在网络受限时仍能获取最新版本。
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
                using var resp = await HttpRequest.Create(url)
                    .SendAsync(retryTimes: 1, cancellationToken: timeoutCts.Token)
                    .ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode) continue;
                var manifest = await resp.AsJsonAsync<PluginMarketManifest>(cancellationToken: timeoutCts.Token)
                    .ConfigureAwait(false);
                if (manifest is not null)
                {
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

    private static IReadOnlyList<(string Url, string Label)> GetManifestFetchCandidates(string manifestUrl)
    {
        var candidates = new List<(string Url, string Label)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 优先：通过 RewriteByConfig 应用用户配置的镜像
        candidates.Add((manifestUrl, "configured"));
        seen.Add(manifestUrl);

        // 回退：所有 GitHub 加速镜像
        if (GitHubAccelerator.ShouldRewrite(manifestUrl))
        {
            foreach (var mirror in GitHubAccelerator.Mirrors)
            {
                var mirrored = mirror + manifestUrl;
                if (seen.Add(mirrored))
                    candidates.Add((mirrored, mirror.TrimEnd('/')));
            }
        }

        return candidates;
    }

    public static PluginMarketVersion SelectCompatibleManifestVersion(PluginMarketManifest manifest, string? currentHostVersion = null)
    {
        if (manifest is null) throw new ArgumentNullException(nameof(manifest));

        var candidates = (manifest.Versions ?? [])
            .Where(version => LooksLikePackageUrl(version.PackageUrl))
            .Select((version, index) => new
            {
                Version = version,
                Index = index,
                ParsedVersion = TryParsePackageVersion(version.Version)
            })
            .ToList();

        if (candidates.Count == 0)
            throw new InvalidDataException("插件 manifest 缺少指向 .pclx 或 .zip 的 versions 条目。");

        currentHostVersion ??= PluginCompatibility.CurrentHostVersion;
        var selected = candidates
            .Where(candidate => IsManifestVersionCompatible(candidate.Version, currentHostVersion))
            .OrderByDescending(candidate => candidate.ParsedVersion is not null)
            .ThenByDescending(candidate => candidate.ParsedVersion)
            .ThenBy(candidate => candidate.Index)
            .FirstOrDefault();

        if (selected is null)
            throw new InvalidDataException("插件 manifest 中没有兼容当前启动器和插件 API 的版本。");

        return selected.Version;
    }

    public static async Task<PluginPreparedInstall> PreparePackageAsync(string packageUrl, string? expectedSha256 = null, CancellationToken ct = default)
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
            using (var response = await HttpRequest.Create(packageUrl)
                       .SendAsync(httpCompletionOption: HttpCompletionOption.ResponseHeadersRead, cancellationToken: ct)
                       .ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                await using var remoteStream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                await using var fileStream = File.Create(packagePath);
                await remoteStream.CopyToAsync(fileStream, ct).ConfigureAwait(false);
            }

            ValidateSha256(packagePath, expectedSha256);
            await Task.Run(() => ExtractZipSafely(packagePath, extractDir), ct).ConfigureAwait(false);
            var pluginRoot = FindPluginRoot(extractDir)
                ?? throw new InvalidDataException("插件包中未找到 plugin.json。请确认该文件是 PCL 插件包。");
            var (manifest, result) = await PluginPackageService.ReadAndValidateDirectoryAsync(pluginRoot, ct).ConfigureAwait(false);
            if (!result.IsValid || manifest is null)
                throw new InvalidDataException(result.ErrorMessage ?? "插件包校验失败。");

            return new PluginPreparedInstall(pluginRoot, manifest, PluginInstallSourceType.Repository, packageUrl, "远程插件包", workDir);
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
        if (PluginCompatibility.TryGetApiCompatibilityError(version.MinApiVersion, version.MaxApiVersion, out _))
            return false;

        if (PluginCompatibility.TryGetHostCompatibilityError(version.MinHostVersion, version.MaxHostVersion, currentHostVersion, out _))
            return false;

        return true;
    }

    private static Version? TryParsePackageVersion(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) && Version.TryParse(value, out var version)
            ? version
            : null;
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

    private static void ExtractZipSafely(string archivePath, string destinationDirectory)
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

    private static void ValidateSha256(string filePath, string? expectedSha256)
    {
        if (string.IsNullOrWhiteSpace(expectedSha256)) return;

        using var stream = File.OpenRead(filePath);
        var actual = Convert.ToHexString(SHA256.HashData(stream));
        var expected = expectedSha256.Trim().Replace(" ", string.Empty).Replace("-", string.Empty);
        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("插件包 SHA-256 校验失败。");
    }

    public static string RewriteGitCloneUrl(string cloneUrl, int mirror)
    {
        return GitHubAccelerator.Rewrite(cloneUrl, mirror);
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
        string cleanupPath)
    {
        PluginRoot = pluginRoot;
        Manifest = manifest;
        SourceType = sourceType;
        SourceUrl = sourceUrl;
        SourceLabel = sourceLabel;
        CleanupPath = cleanupPath;
    }

    public string PluginRoot { get; }

    public PluginPackageManifest Manifest { get; }

    public PluginInstallSourceType SourceType { get; }

    public string SourceUrl { get; }

    public string SourceLabel { get; }

    public string CleanupPath { get; }

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