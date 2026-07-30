using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace PCL.Core.UI.Animation.Clock;

public sealed partial class WinMMClock(int fps = 60) : IClock, IDisposable
{
    private uint _timerId;
    private long _frameIndex;
    private TimeProc? _callback;
    private readonly object _syncRoot = new();
    
    public event EventHandler<long>? Tick;

    public int Fps { get; set; } = fps;
    
    public bool IsRunning { get; private set; }
    
    ~WinMMClock()
    {
        Dispose();
    }
    
    public void Start()
    {
        lock (_syncRoot)
        {
            if (IsRunning) return;

            _frameIndex = 0;
            var delay = (uint)Math.Max(1, 1000.0 / Fps);
            _callback = (_, _, _, _, _) =>
            {
                _frameIndex++;
                Tick?.Invoke(this, _frameIndex);
            };

            _timerId = _TimeSetEvent(
                delay,
                0,
                _callback,
                IntPtr.Zero,
                TimePeriodic | TimeCallbackFunction | TimeKillSynchronous);
            if (_timerId == 0)
            {
                _callback = null;
                throw new Win32Exception("无法创建多媒体动画计时器");
            }

            IsRunning = true;
        }
    }

    public void Stop()
    {
        lock (_syncRoot)
        {
            if (!IsRunning) return;

            // TIME_KILL_SYNCHRONOUS ensures the delegate stays valid until callbacks finish.
            if (_timerId != 0)
            {
                _TimeKillEvent(_timerId);
                _timerId = 0;
            }
            IsRunning = false;
            _callback = null;
        }
    }
    
    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }
    
    [LibraryImport("winmm.dll", EntryPoint = "timeSetEvent", SetLastError = true)]
    private static partial uint _TimeSetEvent(uint uDelay, uint uResolution, TimeProc lpTimeProc, IntPtr dwUser, uint fuEvent);

    [LibraryImport("winmm.dll", EntryPoint = "timeKillEvent", SetLastError = true)]
    private static partial void _TimeKillEvent(uint uTimerId);
    
    private delegate void TimeProc(uint id, uint msg, IntPtr user, IntPtr dw1, IntPtr dw2);
    
    private const uint TimePeriodic = 0x0001;
    private const uint TimeCallbackFunction = 0x0000;
    private const uint TimeKillSynchronous = 0x0100;
}
