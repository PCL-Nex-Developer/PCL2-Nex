using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PCL.Core.App;
using PCL.Core.App.Cli;
using PCL.Core.App.Essentials;
using PCL.Core.App.Localization;
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
                    HintService.Hint(Lang.Text("UriAction.Error.UnknownAction", action), HintType.Error);
                    break;
            }
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "处理 URI 动作失败", ModBase.LogLevel.Feedback);
            HintService.Hint(Lang.Text("UriAction.Error.ActionExecutionFailed", ex.Message), HintType.Error);
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
            HintService.Hint(Lang.Text("UriAction.Error.MissingServerParam"), HintType.Error);
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
            HintService.Hint(Lang.Text("UriAction.Error.MissingVersionParam"), HintType.Error);
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

        HintService.Hint(Lang.Text("UriAction.Error.MissingModpackParam"), HintType.Error);
    }

    private static void DownloadAndInstallModpack(string url, string? instanceName, string? logo, string? resourceId)
    {
        if (!IsAbsoluteHttpUri(url))
        {
            HintService.Hint(Lang.Text("UriAction.Error.InvalidModpackUrl"), HintType.Error);
            return;
        }

        var extension = Path.GetExtension(new Uri(url).AbsolutePath);
        if (string.IsNullOrWhiteSpace(extension)) extension = ".zip";
        var tempDir = Path.Combine(Paths.Temp, "UriModpacks");
        Directory.CreateDirectory(tempDir);
        var target = Path.Combine(tempDir, Guid.NewGuid().ToString("N") + extension);

        var loader = new ModLoader.LoaderCombo<string>(Lang.Text("UriAction.Title.ModpackInstall"), [
            new LoaderDownload(Lang.Text("UriAction.Title.DownloadModpack"), [new DownloadFile([url], target)]) { ProgressWeight = 10d, block = true },
            new ModLoader.LoaderTask<int, int>(Lang.Text("UriAction.Title.InstallingModpack"), _ => ModModpack.ModpackInstall(target, instanceName, logo, resourceId, true)) { ProgressWeight = 0.1d }
        ])
        {
            OnStateChanged = myLoader =>
            {
                if (myLoader.State == ModBase.LoadState.Failed)
                    HintService.Hint(Lang.Text("UriAction.Error.ModpackInstallFailed", myLoader.Error.Message), HintType.Error);
                else if (myLoader.State == ModBase.LoadState.Finished)
                    HintService.Hint(Lang.Text("UriAction.Success.ModpackInstallComplete"), HintType.Success);
            }
        };
        loader.Start(target);
        ModLoader.LoaderTaskbarAdd(loader);
        ModMain.frmMain.BtnExtraDownload.ShowRefresh();
        ModMain.frmMain.BtnExtraDownload.Ribble();
    }

    private static void InstallPlugin(UriActionRequest request)
    {
        if (!PluginUriSourceParser.TryParseInstallSource(request, out var source, out var error))
        {
            HintService.Hint(error ?? Lang.Text("UriAction.Error.InvalidPluginSource"), HintType.Error);
            return;
        }

        _ = InstallPluginAsync(source!);
    }

    private static async System.Threading.Tasks.Task InstallPluginAsync(PluginUriInstallSource source)
    {
        try
        {
            PluginPreparedInstall prepared = source.Kind switch
            {
                PluginUriInstallSourceKind.LocalPackage => await PluginLocalInstallService.PrepareZipAsync(source.Value),
                PluginUriInstallSourceKind.RemotePackage => await PluginRemoteInstallService.PreparePackageAsync(source.Value),
                PluginUriInstallSourceKind.Manifest => await PluginRemoteInstallService.PrepareManifestAsync(source.Value),
                PluginUriInstallSourceKind.Git => await PluginRemoteInstallService.PrepareGitRepositoryAsync(source.Value),
                _ => throw new NotSupportedException(Lang.Text("UriAction.Error.UnsupportedPluginSourceType"))
            };

            using (prepared)
            {
                var manifest = prepared.Manifest;
                var confirm = ModBase.RunInUiWait(() => ModMain.MyMsgBox(
                    Lang.Text("UriAction.Warning.PluginInstallSecurity", prepared.SourceLabel, manifest.Name, prepared.SourceUrl),
                    Lang.Text("UriAction.Action.ConfirmInstall"), button2: "取消", isWarn: true));
                if (confirm != 1) return;

                await PluginInstallService.InstallFromDirectoryAsync(prepared.PluginRoot, manifest, prepared.SourceType,
                    prepared.SourceUrl, installedSha256: prepared.VerifiedSha256);
                ModBase.RunInUi(() => ModMain.MyMsgBox(Lang.Text("UriAction.Success.PluginInstalled", manifest.Name), Lang.Text("UriAction.Title.InstallComplete")));
            }
        }
        catch (Exception ex)
        {
            ModBase.Log(ex, "URI 安装插件失败", ModBase.LogLevel.Feedback);
            HintService.Hint(Lang.Text("UriAction.Error.PluginInstallFailedWithReason", ex.Message), HintType.Error);
        }
    }

    private static void AddPluginSource(UriActionRequest request)
    {
        if (!PluginUriSourceParser.TryParseRepositorySource(request, out var source, out var error))
        {
            HintService.Hint(error ?? Lang.Text("UriAction.Error.InvalidPluginSource2"), HintType.Error);
            return;
        }

        if (PluginTrustService.IsOfficialRepository(source!.Value))
        {
            HintService.Hint(Lang.Text("UriAction.Info.OfficialSourceBuiltIn"), HintType.Success);
            return;
        }

        var confirm = ModBase.RunInUiWait(() => ModMain.MyMsgBox(
            Lang.Text("UriAction.Warning.AddPluginSource", source.Name, source.Kind, source.Value),
            Lang.Text("UriAction.Action.ConfirmAddSource"), button2: "取消", isWarn: true));
        if (confirm != 1) return;

        PluginTrustService.AddTrust(source.Value, source.Name, PluginRepositorySourceType.Custom, source.Kind);
        HintService.Hint(Lang.Text("UriAction.Success.SourceAdded", source.Name), HintType.Success);
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

}
