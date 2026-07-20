using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PCL.Core.App;
using PCL.Core.App.Cli;
using PCL.Core.App.Essentials;
using PCL.Core.App.Plugins;
using PCL.Network;
using PCL.Network.Loaders;

namespace PCL;

public static class UriActionService
{
    public static void Register()
    {
        StartupService.TryHandleCommand("uri", HandleCommand, true);
    }

    private static void HandleCommand(CommandLine model, bool isCallback)
    {
        try
        {
            var request = GetRequest(model);
            if (request is null) return;

            var action = NormalizeAction(request.ActionType ?? request.Command);
            if (string.IsNullOrWhiteSpace(action)) return;

            switch (action)
            {
                case "launch":
                case "launch-game":
                case "play":
                    LaunchGame(request);
                    break;
                case "launch-server":
                case "join-server":
                case "join":
                    LaunchGame(request, requireServer: true);
                    break;
                case "download-vanilla":
                case "download-minecraft":
                case "install-vanilla":
                case "install-minecraft":
                    DownloadVanilla(request);
                    break;
                case "install-modpack":
                case "download-modpack":
                case "modpack":
                    InstallModpack(request);
                    break;
                case "install-plugin":
                case "plugin-install":
                    InstallPlugin(request);
                    break;
                case "add-plugin-source":
                case "add-plugin-repo":
                case "add-plugin-repository":
                case "plugin-source":
                    AddPluginSource(request);
                    break;
                default:
                    HintService.Hint($"未知 URI 动作：{action}", HintType.Error);
                    break;
            }
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "处理 URI 动作失败", ModBase.LogLevel.Feedback);
            HintService.Hint("URI 动作执行失败：" + ex.Message, HintType.Error);
        }
    }

    private static UriActionRequest? GetRequest(CommandLine model)
    {
        var rawUri = GetTextArgument(model, "uri");
        if (!string.IsNullOrWhiteSpace(rawUri) && UriSchemeService.TryParseUriAction(rawUri, out var request))
            return request;

        var action = GetTextArgument(model, "action")
                     ?? GetTextArgument(model, "name")
                     ?? GetTextArgument(model, "type")
                     ?? GetTextArgument(model, "event");
        if (string.IsNullOrWhiteSpace(action)) return null;

        var query = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in model.Arguments.Keys)
        {
            var value = GetTextArgument(model, key);
            if (value is not null) query[key] = value;
        }

        var data = GetFirstValue(query, "data", "arg", "args", "parameter", "value");
        return new UriActionRequest("cli", action, action, data, "", [], query);
    }

    private static void LaunchGame(UriActionRequest request, bool requireServer = false)
    {
        var instanceName = GetFirstValue(request, "instance", "version", "name") ?? request.PathArguments.FirstOrDefault();
        var server = GetFirstValue(request, "server", "serverIp", "ip", "address");
        var world = GetFirstValue(request, "world", "worldName");

        if (requireServer && string.IsNullOrWhiteSpace(server))
        {
            HintService.Hint("URI 启动服务器缺少 server 参数。", HintType.Error);
            return;
        }

        ModBase.RunInUi(() =>
        {
            var options = new ModLaunch.McLaunchOptions
            {
                instance = string.IsNullOrWhiteSpace(instanceName) ? null : new McInstance(instanceName),
                ServerIp = string.IsNullOrWhiteSpace(server) ? null : server,
                WorldName = string.IsNullOrWhiteSpace(world) ? null : world
            };
            ModLaunch.McLaunchStart(options);
        }, forceWaitUntilLoaded: true);
    }

    private static void DownloadVanilla(UriActionRequest request)
    {
        var version = GetFirstValue(request, "version", "id", "name") ?? request.PathArguments.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(version))
        {
            HintService.Hint("URI 下载原版缺少 version 参数。", HintType.Error);
            return;
        }

        var jsonUrl = GetFirstValue(request, "jsonUrl", "json", "manifest");
        var force = IsTruthy(GetFirstValue(request, "force", "overwrite"));
        var behaviour = force ? NetPreDownloadBehaviour.IgnoreCheck : NetPreDownloadBehaviour.ExitWhileExistsOrDownloading;
        ModBase.RunInUi(() => ModDownloadLib.McDownloadClient(behaviour, version, jsonUrl), forceWaitUntilLoaded: true);
    }

    private static void InstallModpack(UriActionRequest request)
    {
        var file = GetFirstValue(request, "file", "path") ?? request.PathArguments.FirstOrDefault();
        var url = GetFirstValue(request, "url", "source");
        var instanceName = GetFirstValue(request, "name", "instance", "instanceName");
        var logo = GetFirstValue(request, "logo");
        var resourceId = GetFirstValue(request, "resourceId", "projectId");

        if (!string.IsNullOrWhiteSpace(file) && File.Exists(file))
        {
            ModBase.RunInThread(() => ModModpack.ModpackInstall(file, instanceName, logo, resourceId, false));
            return;
        }

        if (string.IsNullOrWhiteSpace(url) && IsAbsoluteHttpUri(file)) url = file;
        if (!string.IsNullOrWhiteSpace(url))
        {
            DownloadAndInstallModpack(url, instanceName, logo, resourceId);
            return;
        }

        HintService.Hint("URI 安装整合包缺少 file 或 url 参数。", HintType.Error);
    }

    private static void DownloadAndInstallModpack(string url, string? instanceName, string? logo, string? resourceId)
    {
        if (!IsAbsoluteHttpUri(url))
        {
            HintService.Hint("URI 整合包下载地址无效。", HintType.Error);
            return;
        }

        var extension = Path.GetExtension(new Uri(url).AbsolutePath);
        if (string.IsNullOrWhiteSpace(extension)) extension = ".zip";
        var tempDir = Path.Combine(Paths.Temp, "UriModpacks");
        Directory.CreateDirectory(tempDir);
        var target = Path.Combine(tempDir, Guid.NewGuid().ToString("N") + extension);

        var loader = new ModLoader.LoaderCombo<string>("URI 整合包安装", [
            new LoaderDownload("下载整合包文件", [new DownloadFile([url], target)]) { ProgressWeight = 10d, block = true },
            new ModLoader.LoaderTask<int, int>("安装整合包", _ => ModModpack.ModpackInstall(target, instanceName, logo, resourceId, true)) { ProgressWeight = 0.1d }
        ])
        {
            OnStateChanged = myLoader =>
            {
                if (myLoader.State == ModBase.LoadState.Failed)
                    HintService.Hint("URI 整合包安装失败：" + myLoader.Error.Message, HintType.Error);
                else if (myLoader.State == ModBase.LoadState.Finished)
                    HintService.Hint("URI 整合包安装已完成", HintType.Success);
            }
        };
        loader.Start(target);
        ModLoader.LoaderTaskbarAdd(loader);
        ModMain.frmMain.BtnExtraDownload.ShowRefresh();
        ModMain.frmMain.BtnExtraDownload.Ribble();
    }

    private static void InstallPlugin(UriActionRequest request)
    {
        var source = GetFirstValue(request, "source", "url", "git", "file", "path") ?? request.PathArguments.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(source))
        {
            HintService.Hint("URI 安装插件缺少 source 参数。", HintType.Error);
            return;
        }

        _ = InstallPluginAsync(source);
    }

    private static async System.Threading.Tasks.Task InstallPluginAsync(string source)
    {
        try
        {
            PluginPreparedInstall prepared;
            if (File.Exists(source) && IsPluginArchivePath(source)) prepared = await PluginLocalInstallService.PrepareZipAsync(source);
            else if (source.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || source.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                prepared = await PluginRemoteInstallService.PrepareAsync(source);
            else
            {
                HintService.Hint("URI 插件来源无效。仅支持 .pclx/.zip 文件或远程 manifest/package URL。", HintType.Error);
                return;
            }

            using (prepared)
            {
                var manifest = prepared.Manifest;
                var confirm = ModBase.RunInUiWait(() => ModMain.MyMsgBox(
                    "即将安装插件（" + prepared.SourceLabel + "）：\n\n名称: " + manifest.Name + "\n来源: " + prepared.SourceUrl + "\n\n重大安全提醒：插件会在启动器内运行代码，可能读取或修改本地文件、访问网络、修改启动器界面，甚至执行恶意操作。\n请只安装你完全信任的来源。",
                    "确认安装", button2: "取消", isWarn: true));
                if (confirm != 1) return;

                await PluginInstallService.InstallFromDirectoryAsync(prepared.PluginRoot, manifest, prepared.SourceType,
                    prepared.SourceUrl, installedSha256: prepared.VerifiedSha256);
                ModBase.RunInUi(() => ModMain.MyMsgBox("插件 " + manifest.Name + " 安装成功！", "安装完成"));
            }
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "URI 安装插件失败", ModBase.LogLevel.Feedback);
            HintService.Hint("URI 安装插件失败：" + ex.Message, HintType.Error);
        }
    }

    private static void AddPluginSource(UriActionRequest request)
    {
        var url = GetFirstValue(request, "url", "source", "repo", "repository", "index", "registry") ?? request.PathArguments.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(url))
        {
            HintService.Hint("URI 添加插件源缺少 url 参数。", HintType.Error);
            return;
        }

        url = url.Trim();
        if (!IsAbsoluteHttpUri(url))
        {
            HintService.Hint("URI 插件源地址无效。仅支持 HTTP 或 HTTPS 地址。", HintType.Error);
            return;
        }

        if (PluginTrustService.IsOfficialRepository(url))
        {
            HintService.Hint("官方插件源已内置，无需重复添加。", HintType.Success);
            return;
        }

        var name = GetFirstValue(request, "name", "title", "repoName") ?? "自定义插件源";
        name = string.IsNullOrWhiteSpace(name) ? "自定义插件源" : name.Trim();
        var confirm = ModBase.RunInUiWait(() => ModMain.MyMsgBox(
            "即将添加第三方插件源：\n\n名称: " + name + "\n地址: " + url + "\n\n插件源会影响商店中可展示和可更新的插件。请只添加你信任的来源。",
            "确认添加插件源", button2: "取消", isWarn: true));
        if (confirm != 1) return;

        PluginTrustService.AddTrust(url, name, PluginRepositorySourceType.Custom);
        HintService.Hint("插件源已添加：" + name, HintType.Success);
    }

    private static string? GetTextArgument(CommandLine model, string key)
    {
        var (exists, isTypeMatch) = model.TryGetArgumentValue<string>(key, out var value);
        return exists && isTypeMatch ? value : null;
    }

    private static string? GetFirstValue(UriActionRequest request, params string[] keys)
        => GetFirstValue(request.Query, keys);

    private static string? GetFirstValue(IReadOnlyDictionary<string, string> values, params string[] keys)
    {
        foreach (var key in keys)
            if (values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
                return value;
        return null;
    }

    private static string NormalizeAction(string action)
        => action.Trim().Replace('_', '-').ToLowerInvariant();

    private static bool IsTruthy(string? value)
        => value is not null && (value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
                                 value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                                 value.Equals("yes", StringComparison.OrdinalIgnoreCase));

    private static bool IsAbsoluteHttpUri(string? value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
           (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private static bool IsPluginArchivePath(string path)
        => path.EndsWith(".pclx", StringComparison.OrdinalIgnoreCase)
           || path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
}
