using System;
using System.Threading;
using System.Windows.Media;
using System.Windows.Threading;

namespace PCL.Core.UI.Animation.Clock;

/// <summary>
/// 一个基于 WPF CompositionTarget.Rendering 事件的时钟实现。
/// 该时钟引发的所有事件均在 UI 线程上执行。
/// </summary>
public sealed class WpfCompositionTargetRenderingClock : IUIClock, IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly object _stateLock = new();
    private int _isRunning;
    private int _stateVersion;
    private int _appliedVersion = -1;
    private int _renderingVersion = -1;
    private bool _isSubscribed;
    private TimeSpan _startTime;
    private TimeSpan _lastRenderingTime;
    private long _lastFrame = -1;

    public WpfCompositionTargetRenderingClock(Dispatcher dispatcher, int fps = 60)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        Fps = fps;
    }

    public event EventHandler<long>? Tick;

    public int Fps { get; set; }

    public bool IsRunning => Volatile.Read(ref _isRunning) != 0;

    public void Start()
    {
        lock (_stateLock)
        {
            if (IsRunning)
                return;
            if (_dispatcher.HasShutdownStarted || _dispatcher.HasShutdownFinished)
                throw new InvalidOperationException("WPF Dispatcher 已停止，无法启动动画时钟");

            Interlocked.Exchange(ref _isRunning, 1);
            _stateVersion++;
        }

        ScheduleStateUpdate();
    }

    private void OnRendering(object? sender, EventArgs args)
    {
        if (args is not RenderingEventArgs renderingArgs)
            return;

        lock (_stateLock)
        {
            if (!IsRunning || _renderingVersion != _stateVersion)
                return;

            var renderingTime = renderingArgs.RenderingTime;
            if (renderingTime == _lastRenderingTime)
                return;

            long frame;
            if (Fps == int.MaxValue)
            {
                frame = _lastFrame + 1;
            }
            else
            {
                if (_startTime == TimeSpan.MinValue)
                    _startTime = renderingTime;
                var elapsedTicks = renderingTime.Ticks - _startTime.Ticks;
                frame = (elapsedTicks + TimeSpan.TicksPerMillisecond / 2) * Fps /
                        TimeSpan.TicksPerSecond;
                if (frame <= _lastFrame)
                    return;
            }

            _lastRenderingTime = renderingTime;
            _lastFrame = frame;
            Tick?.Invoke(this, frame);
        }
    }

    public void Stop()
    {
        lock (_stateLock)
        {
            if (!IsRunning)
                return;

            Interlocked.Exchange(ref _isRunning, 0);
            _stateVersion++;
        }
        if (_dispatcher.HasShutdownStarted || _dispatcher.HasShutdownFinished)
            return;

        ScheduleStateUpdate();
    }

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }

    private void ScheduleStateUpdate()
    {
        var version = Volatile.Read(ref _stateVersion);
        if (_dispatcher.CheckAccess())
        {
            ApplyState(version);
        }
        else
        {
            try
            {
                var operation = _dispatcher.BeginInvoke(() => ApplyState(version), DispatcherPriority.Send);
                operation.Aborted += (_, _) =>
                {
                    RollBackAbortedStart(version);
                };
                if (operation.Status == DispatcherOperationStatus.Aborted)
                    RollBackAbortedStart(version);
            }
            catch
            {
                RollBackAbortedStart(version);
                throw;
            }
        }
    }

    private void ApplyState(int version)
    {
        lock (_stateLock)
        {
            if (version != _stateVersion || _appliedVersion == version)
                return;
            _appliedVersion = version;

            if (IsRunning)
            {
                _startTime = TimeSpan.MinValue;
                _lastRenderingTime = TimeSpan.MinValue;
                _lastFrame = -1;
                _renderingVersion = version;
                if (_isSubscribed)
                    return;
                CompositionTarget.Rendering += OnRendering;
                _isSubscribed = true;
            }
            else if (_isSubscribed)
            {
                CompositionTarget.Rendering -= OnRendering;
                _isSubscribed = false;
                _renderingVersion = -1;
            }
        }
    }

    private void RollBackAbortedStart(int version)
    {
        lock (_stateLock)
        {
            if (version == _stateVersion && IsRunning)
                Interlocked.Exchange(ref _isRunning, 0);
        }
    }
}
