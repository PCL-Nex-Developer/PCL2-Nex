using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PCL.Core.App.Localization;

namespace PCL.Core.App.Plugins;

/// <summary>
/// 准备来自本地插件包的安装源。
/// </summary>
public static class PluginLocalInstallService
{
    public static async Task<PluginPreparedInstall> PrepareZipAsync(string archivePath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(archivePath) || !File.Exists(archivePath))
            throw new FileNotFoundException(Text("Plugins.LocalInstall.Error.ZipNotFound", "插件 zip 文件不存在。"), archivePath);

        var workDir = Path.Combine(PCL.Core.App.Paths.PluginTemp, "local_" + Guid.NewGuid().ToString("N"));
        var extractDir = Path.Combine(workDir, "extract");
        Directory.CreateDirectory(extractDir);

        try
        {
            var verifiedSha256 = PluginRemoteInstallService.ValidateSha256(archivePath, null);
            await Task.Run(() => ExtractZipSafely(archivePath, extractDir), ct).ConfigureAwait(false);
            var pluginRoot = FindPluginRoot(extractDir)
                ?? throw new InvalidDataException(Text("Plugins.LocalInstall.Error.NoPluginJson", "zip 中未找到 plugin.json。请确认该 zip 是 PCL 插件包。"));
            var (manifest, result) = await PluginPackageService.ReadAndValidateDirectoryAsync(pluginRoot, ct).ConfigureAwait(false);
            if (!result.IsValid || manifest is null)
                throw new InvalidDataException(result.ErrorMessage ?? Text("Plugins.LocalInstall.Error.DirectoryValidationFailed", "插件目录校验失败。"));

            var sourceLabel = archivePath.EndsWith(".pclx", StringComparison.OrdinalIgnoreCase) ? Text("Plugins.LocalInstall.Label.LocalPclx", "本地 pclx") : Text("Plugins.LocalInstall.Label.LocalZip", "本地 zip");
            return new PluginPreparedInstall(pluginRoot, manifest, PluginInstallSourceType.Local,
                archivePath, sourceLabel, workDir, verifiedSha256);
        }
        catch
        {
            try { if (Directory.Exists(workDir)) Directory.Delete(workDir, recursive: true); }
            catch { }
            throw;
        }
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
                throw new InvalidDataException(Text("Plugins.LocalInstall.Error.UnsafePath", "zip 包包含不安全的路径。"));

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(targetPath);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            entry.ExtractToFile(targetPath, overwrite: true);
        }
    }

    private static string Text(string key, string fallback, params object?[] args)
    {
        var template = Lang.Text(key);
        if (string.Equals(template, key, StringComparison.Ordinal)
            || string.Equals(template, $"!{key}!", StringComparison.Ordinal))
            template = fallback;
        return string.Format(Lang.Culture, template, args);
    }
}
