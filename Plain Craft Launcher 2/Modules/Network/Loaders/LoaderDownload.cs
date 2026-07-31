using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PCL.Core.Utils;

namespace PCL.Network.Loaders;

public class LoaderDownload : ModLoader.LoaderBase
{
    public ModBase.SafeList<PCL.Network.DownloadFile> files;
    private CancellationTokenSource? _cancellationTokenSource;
    private IReadOnlyList<PCL.Network.DownloadFile> _activeFiles = [];
    private Task _activeRunTask = Task.CompletedTask;
    private int _runId;
    private bool _isCompleting;
    public int FailCount { get; set; }

    public override double Progress
    {
        get
        {
            if (State >= ModBase.LoadState.Finished)
                return 1;

            var snapshot = files.ToList();
            return snapshot.Count > 0 ? snapshot.Average(file => file.Progress) : 0;
        }
        set => throw new Exception("文件下载不允许指定进度");
    }

    public LoaderDownload(string name, List<PCL.Network.DownloadFile> fileTasks)
    {
        base.name = name;
        files = new ModBase.SafeList<PCL.Network.DownloadFile>(fileTasks ?? new List<PCL.Network.DownloadFile>());
    }

    public void RefreshStat() { }

    public override void Start(object input = null, bool isForceRestart = false)
    {
        CancellationTokenSource cancellationTokenSource;
        List<PCL.Network.DownloadFile> fileSnapshot;
        Task previousRunTask;
        TaskCompletionSource runCompletion;
        int runId;
        lock (lockState)
        {
            if (State == ModBase.LoadState.Loading)
                return;

            if (input is List<PCL.Network.DownloadFile> inputFiles)
                files = new ModBase.SafeList<PCL.Network.DownloadFile>(inputFiles);

            fileSnapshot = files.ToList();
            cancellationTokenSource = new CancellationTokenSource();
            previousRunTask = _activeRunTask;
            runCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _activeRunTask = runCompletion.Task;
            _cancellationTokenSource = cancellationTokenSource;
            _activeFiles = fileSnapshot;
            _isCompleting = false;
            runId = ++_runId;
            State = ModBase.LoadState.Loading;
        }

        ModBase.RunInNewThread(() => RunQueued(runId, fileSnapshot, cancellationTokenSource,
            previousRunTask, runCompletion), $"DL/{Uuid}");
    }

    private void RunQueued(int runId, IReadOnlyList<PCL.Network.DownloadFile> fileSnapshot,
        CancellationTokenSource cancellationTokenSource, Task previousRunTask, TaskCompletionSource runCompletion)
    {
        var cancellationToken = cancellationTokenSource.Token;
        try
        {
            previousRunTask.GetAwaiter().GetResult();
            lock (lockState)
            {
                if (runId != _runId || State != ModBase.LoadState.Loading)
                    return;
                ModNet.NetManager.Start(this);
            }

            if (fileSnapshot.Count == 0)
            {
                CompleteSuccessfully(runId);
                return;
            }

            var exceptions = new ConcurrentQueue<Exception>();
            var nextFileIndex = -1;
            var workerCount = GetMaxParallelFiles(fileSnapshot.Count);
            var tasks = Enumerable.Range(0, workerCount).Select(_ => ProcessFilesAsync()).ToArray();

            async Task ProcessFilesAsync()
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var index = Interlocked.Increment(ref nextFileIndex);
                    if (index >= fileSnapshot.Count)
                        return;

                    var file = fileSnapshot[index];
                    try
                    {
                        await ProcessFileAsync(file, fileSnapshot.Count, cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (Exception ex)
                    {
                        file.AddError(ex);
                        file.State = PCL.Network.NetState.Interrupted;
                        file.Speed = 0;
                        file.ActiveThreads = 0;
                        exceptions.Enqueue(ex);
                        cancellationTokenSource.Cancel();
                        return;
                    }
                }
            }

            Task.WhenAll(tasks).GetAwaiter().GetResult();
            if (!exceptions.IsEmpty)
                CompleteWithFailure(runId, fileSnapshot, exceptions.ToList());
            else if (!cancellationToken.IsCancellationRequested)
                CompleteSuccessfully(runId);
        }
        catch (Exception ex)
        {
            CompleteWithFailure(runId, fileSnapshot, [ex]);
        }
        finally
        {
            lock (lockState)
            {
                if (ReferenceEquals(_cancellationTokenSource, cancellationTokenSource))
                    _cancellationTokenSource = null;
            }
            cancellationTokenSource.Dispose();
            runCompletion.TrySetResult();
        }
    }

    private static int GetMaxParallelFiles(int fileCount)
    {
        return Math.Max(1, Math.Min(fileCount, Math.Clamp(ModNet.NetTaskThreadLimit, 1, 64)));
    }

    private async Task ProcessFileAsync(PCL.Network.DownloadFile file, int fileCount,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        file.RegisterLoader(this);

        Directory.CreateDirectory(Path.GetDirectoryName(file.LocalPath) ?? throw new IOException("下载路径无效"));
        if (file.Check?.canUseExistsFile == true && file.Check.Check(file.LocalPath) is null)
        {
            file.IsCopy = true;
            file.State = PCL.Network.NetState.Finished;
            try { file.TotalSize = new FileInfo(file.LocalPath).Length; }
            catch (IOException) { file.TotalSize = -1; }
            file.DownloadedBytes = file.TotalSize;
            file.Speed = 0;
            file.ActiveThreads = 0;
            return;
        }

        file.State = PCL.Network.NetState.Connecting;
        var enableParallelChunks = fileCount <= 1;
        for (var retry = 0; retry < 4; retry++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await FileDownloader.DownloadAsync(file.Urls, file.LocalPath, file.UseBrowserUserAgent, file.CustomUserAgent,
                    cancellationToken, enableParallelChunks, file).ConfigureAwait(false);
                break;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (retry < 3)
            {
                ModBase.Log(ex, $"[Download] 重试 {retry + 1}/3：{file.LocalPath}", ModBase.LogLevel.Debug);
                await Task.Delay(RandomUtils.NextInt(300, 500 + retry * 300), cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        try { file.TotalSize = new FileInfo(file.LocalPath).Length; }
        catch (IOException) { file.TotalSize = -1; }
        file.IsUnknownSize = file.TotalSize < 0;
        file.DownloadedBytes = Math.Max(0, file.TotalSize);
        file.Speed = 0;
        file.ActiveThreads = 0;
        file.State = PCL.Network.NetState.Finished;
    }

    public void OnFileFinish(PCL.Network.DownloadFile file)
    {
        int runId;
        IReadOnlyList<PCL.Network.DownloadFile> fileSnapshot;
        lock (lockState)
        {
            runId = _runId;
            fileSnapshot = _activeFiles;
            if (fileSnapshot.Any(activeFile => activeFile.State != PCL.Network.NetState.Finished))
                return;
        }

        CompleteSuccessfully(runId);
    }

    public void OnFinish()
    {
        int runId;
        lock (lockState)
            runId = _runId;

        CompleteSuccessfully(runId);
    }

    public void OnFileFail(PCL.Network.DownloadFile file)
    {
        var errors = file.Errors;
        OnFail(errors.Count > 0
            ? errors.ToList()
            : [new Exception($"文件下载失败：{file.LocalPath}")]);
    }

    public void OnFail(List<Exception> exList)
    {
        int runId;
        IReadOnlyList<PCL.Network.DownloadFile> fileSnapshot;
        lock (lockState)
        {
            runId = _runId;
            fileSnapshot = _activeFiles;
        }

        CompleteWithFailure(runId, fileSnapshot, exList);
    }

    private void CompleteSuccessfully(int runId)
    {
        lock (lockState)
        {
            if (runId != _runId || State != ModBase.LoadState.Loading || _isCompleting)
                return;
            _isCompleting = true;
        }

        try
        {
            RaisePreviewFinish();
        }
        catch (Exception ex)
        {
            CompleteClaimedFailure(runId, [ex]);
            return;
        }

        lock (lockState)
        {
            if (runId != _runId || State != ModBase.LoadState.Loading || !_isCompleting)
                return;

            ModNet.NetManager.Finish(this);
            State = ModBase.LoadState.Finished;
            _isCompleting = false;
        }
    }

    private void CompleteWithFailure(int runId, IReadOnlyList<PCL.Network.DownloadFile> fileSnapshot,
        IReadOnlyList<Exception> exceptions)
    {
        lock (lockState)
        {
            if (runId != _runId || State != ModBase.LoadState.Loading || _isCompleting)
                return;

            _isCompleting = true;
            CompleteFailureCore(fileSnapshot, exceptions);
            _isCompleting = false;
        }
    }

    private void CompleteClaimedFailure(int runId, IReadOnlyList<Exception> exceptions)
    {
        lock (lockState)
        {
            if (runId != _runId || State != ModBase.LoadState.Loading || !_isCompleting)
                return;

            CompleteFailureCore(_activeFiles, exceptions);
            _isCompleting = false;
        }
    }

    private void CompleteFailureCore(IReadOnlyList<PCL.Network.DownloadFile> fileSnapshot,
        IReadOnlyList<Exception> exceptions)
    {
        Error = exceptions.FirstOrDefault() ?? new Exception("未知下载错误");
        FailCount += exceptions.Count;
        foreach (var file in fileSnapshot.Where(file => file.State != PCL.Network.NetState.Finished))
        {
            file.State = PCL.Network.NetState.Interrupted;
            file.Speed = 0;
            file.ActiveThreads = 0;
            file.AddErrors(exceptions);
        }

        ModNet.NetManager.Finish(this);
        State = ModBase.LoadState.Failed;
    }

    public override void Abort()
    {
        lock (lockState)
        {
            if (State >= ModBase.LoadState.Finished || _isCompleting)
                return;

            _cancellationTokenSource?.Cancel();
            foreach (var file in _activeFiles.Where(file => file.State != PCL.Network.NetState.Finished))
            {
                file.State = PCL.Network.NetState.Interrupted;
                file.Speed = 0;
                file.ActiveThreads = 0;
            }

            ModNet.NetManager.Finish(this);
            State = ModBase.LoadState.Aborted;
        }
    }
}
