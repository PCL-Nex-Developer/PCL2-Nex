using System.IO;
using PCL.Core.App;
using PCL.Core.App.Localization;
using PCL.Core.App.Plugins;
using PCL.Core.IO.Net.Http;
using PCL.Network;
using PCL.Network.Loaders;

namespace PCL;

/// <summary>将插件商店安装流程接入启动器任务管理器。</summary>
public static class PluginInstallTaskManager
{
    public static Task<bool> StartStoreInstallAsync(
        PluginRepositoryEntry entry,
        PluginInstallSourceEntry sourceEntry,
        PluginMarketVersion? selectedVersion,
        Func<Task<PluginPreparedInstall>> fallbackPrepare,
        Action? onSuccess = null)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(sourceEntry);
        ArgumentNullException.ThrowIfNull(fallbackPrepare);

        var taskName = Lang.Text("Plugins.Store.Install.TaskName", entry.Name);
        if (ModLoader.loaderTaskbar.Any(loader =>
                loader.State == ModBase.LoadState.Loading
                && string.Equals(loader.name, taskName, StringComparison.Ordinal)))
        {
            HintService.Hint(Lang.Text("Plugins.Store.Install.TaskAlreadyRunning"), HintType.Error);
            return Task.FromResult(false);
        }

        var packagePlan = CreatePackagePlan(entry, sourceEntry, selectedVersion);
        var downloadDirectory = packagePlan is null
            ? null
            : Path.Combine(Paths.PluginTemp, "task_" + Guid.NewGuid().ToString("N"));
        var packagePath = packagePlan is null
            ? null
            : Path.Combine(downloadDirectory!, GetPackageFileName(packagePlan.Url));
        var loaders = new List<ModLoader.LoaderBase>();

        if (packagePlan is not null)
        {
            Directory.CreateDirectory(downloadDirectory!);
            var checker = new ModBase.FileChecker(
                hash: packagePlan.ExpectedSha256,
                canUseExistsFile: false);
            loaders.Add(new LoaderDownload(
                Lang.Text("Plugins.Store.Install.Stage.Download"),
                [new DownloadFile(packagePlan.Urls, packagePath!, checker)])
            {
                ProgressWeight = 6d
            });
        }

        var prepareLoader = new ModLoader.LoaderTask<string, PreparedInstallLease>(
            Lang.Text("Plugins.Store.Install.Stage.Prepare"),
            task =>
            {
                PluginPreparedInstall prepared;
                try
                {
                    prepared = packagePlan is null
                        ? fallbackPrepare().GetAwaiter().GetResult()
                        : PluginRemoteInstallService.PrepareDownloadedPackageAsync(
                                packagePath!,
                                packagePlan.Url,
                                packagePlan.ExpectedSha256,
                                packagePlan.ExpectedPluginId,
                                packagePlan.ExpectedVersion,
                                packagePlan.ExpectedDependencies)
                            .GetAwaiter()
                            .GetResult();
                }
                finally
                {
                    CleanupDirectory(downloadDirectory);
                }

                if (task.IsAborted)
                {
                    prepared.Dispose();
                    throw new ModBase.CancelledException();
                }

                task.output = new PreparedInstallLease(prepared);
            })
        {
            ProgressWeight = packagePlan is null ? 8d : 2d
        };
        loaders.Add(prepareLoader);

        loaders.Add(new ModLoader.LoaderTask<PreparedInstallLease, bool>(
            Lang.Text("Plugins.Store.Install.Stage.Install"),
            task =>
            {
                using var prepared = task.input.Claim();
                try
                {
                    if (task.IsAborted) throw new ModBase.CancelledException();
                    var persistentSource = PluginRepositoryService.GetPersistentInstallSource(
                        entry, sourceEntry, prepared.SourceType, prepared.SourceUrl);
                    PluginInstallService.InstallFromDirectoryAsync(
                            prepared.PluginRoot,
                            prepared.Manifest,
                            persistentSource.Type,
                            persistentSource.Url,
                            installedSha256: prepared.VerifiedSha256)
                        .GetAwaiter()
                        .GetResult();
                    task.output = true;
                }
                catch (OperationCanceledException)
                {
                    throw new ModBase.CancelledException();
                }
            })
        {
            ProgressWeight = 1d
        });

        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var loader = new ModLoader.LoaderCombo<string>(taskName, loaders)
        {
            OnStateChanged = current =>
            {
                switch (current.State)
                {
                    case ModBase.LoadState.Finished:
                        ModMain.frmMain?.RefreshRestartButton(true);
                        HintService.Hint(
                            Lang.Text("Plugins.Store.Install.TaskSuccess", entry.Name),
                            HintType.Success);
                        try { onSuccess?.Invoke(); }
                        catch (Exception ex)
                        {
                            ModBase.Log(ex, "[Plugins] Refresh after task install failed", ModBase.LogLevel.Debug);
                        }
                        prepareLoader.output?.DisposeIfUnclaimed();
                        completion.TrySetResult(true);
                        break;
                    case ModBase.LoadState.Failed:
                        var error = current.Error?.GetBaseException().Message
                                    ?? Lang.Text("Plugins.RemoteInstall.Error.DownloadFailed");
                        ModBase.Log(current.Error, "[Plugins] Store install task failed: " + entry.Id,
                            ModBase.LogLevel.Debug);
                        HintService.Hint(Lang.Text("Plugins.Store.Install.Error", error), HintType.Error);
                        prepareLoader.output?.DisposeIfUnclaimed();
                        CleanupDirectory(downloadDirectory);
                        completion.TrySetResult(false);
                        break;
                    case ModBase.LoadState.Aborted:
                        HintService.Hint(taskName + Lang.Text("Common.Status.Cancelled"));
                        prepareLoader.output?.DisposeIfUnclaimed();
                        CleanupDirectory(downloadDirectory);
                        completion.TrySetResult(false);
                        break;
                }
            }
        };

        loader.Start(entry.Id);
        ModLoader.LoaderTaskbarAdd(loader);
        ModMain.frmMain?.BtnExtraDownload.ShowRefresh();
        ModMain.frmMain?.BtnExtraDownload.Ribble();
        return completion.Task;
    }

    private static PluginPackagePlan? CreatePackagePlan(
        PluginRepositoryEntry entry,
        PluginInstallSourceEntry sourceEntry,
        PluginMarketVersion? selectedVersion)
    {
        var version = selectedVersion ?? entry.SelectedVersion;
        if (version is not null)
        {
            var download = PluginRepositoryService.SelectDownload(
                               version,
                               System.Runtime.InteropServices.RuntimeInformation.OSArchitecture)
                           ?? throw new InvalidDataException(Lang.Text("Plugins.Detail.Message.PackageNotAvailable"));
            if (!PluginRepositoryService.IsValidSha256(download.Sha256))
                throw new InvalidDataException(Lang.Text("Plugins.RemoteInstall.Error.InvalidSha256"));
            version.ResolvedPackageUrl = download.PackageUrl;
            version.ResolvedSha256 = download.Sha256;
            return new PluginPackagePlan(
                download.PackageUrl,
                download.Sha256,
                entry.Id,
                version.Version,
                version.ResolvedDependencies);
        }

        if (!string.Equals(sourceEntry.Type, "package", StringComparison.OrdinalIgnoreCase)) return null;
        if (!PluginRepositoryService.IsValidSha256(sourceEntry.Sha256))
            throw new InvalidDataException(Lang.Text("Plugins.RemoteInstall.Error.MarketPackageSha256Required"));
        return new PluginPackagePlan(sourceEntry.Url, sourceEntry.Sha256!, entry.Id, null, null);
    }

    private static string GetPackageFileName(string url)
    {
        var extension = Uri.TryCreate(url, UriKind.Absolute, out var uri)
            ? Path.GetExtension(uri.AbsolutePath)
            : string.Empty;
        if (!string.Equals(extension, ".pclx", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(extension, ".zip", StringComparison.OrdinalIgnoreCase))
            extension = ".zip";
        return "package" + extension;
    }

    private static void CleanupDirectory(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)) return;
        try { Directory.Delete(directory, recursive: true); }
        catch { }
    }

    public sealed class PreparedInstallLease(PluginPreparedInstall prepared)
    {
        private int _state;

        public PluginPreparedInstall Claim()
        {
            if (Interlocked.CompareExchange(ref _state, 1, 0) != 0)
                throw new InvalidOperationException("Prepared plugin install has already been claimed.");
            return prepared;
        }

        public void DisposeIfUnclaimed()
        {
            if (Interlocked.CompareExchange(ref _state, 2, 0) == 0)
                prepared.Dispose();
        }
    }

    private sealed record PluginPackagePlan(
        string Url,
        string ExpectedSha256,
        string ExpectedPluginId,
        string? ExpectedVersion,
        IReadOnlyList<PluginDependency>? ExpectedDependencies)
    {
        public IReadOnlyList<string> Urls { get; } = [Url];
    }
}
