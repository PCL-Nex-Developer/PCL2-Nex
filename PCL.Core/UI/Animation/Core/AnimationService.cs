using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using PCL.Core.App.IoC;
using PCL.Core.UI.Animation.Animatable;
using PCL.Core.UI.Animation.Clock;
using PCL.Core.UI.Animation.UIAccessProvider;
using PCL.Core.UI.Animation.ValueProcessor;
using PCL.Core.Utils.Threading;

namespace PCL.Core.UI.Animation.Core;

[LifecycleService(LifecycleState.WindowCreating)]
public sealed class AnimationService : GeneralService
{
    #region Lifecycle

    private static LifecycleContext? _context;
    private static LifecycleContext Context => _context!;

    private AnimationService() : base("animation", "动画")
    {
        _context = ServiceContext;
    }
    
    public override void Start()
    {
        _Initialize();
    }

    public override void Stop()
    {
        _Uninitialize();
    }

    #endregion

    private static void _RegisterValueProcessors()
    {
        // 在这里注册所有的 ValueProcessor
        ValueProcessorManager.Register(new DoubleValueProcessor());
        ValueProcessorManager.Register(new MatrixValueProcessor());
        ValueProcessorManager.Register(new NColorValueProcessor());
        ValueProcessorManager.Register(new NRotateTransformValueProcessor());
        ValueProcessorManager.Register(new NScaleTransformValueProcessor());
        ValueProcessorManager.Register(new PointValueProcessor());
        ValueProcessorManager.Register(new ThicknessValueProcessor());
    }

    private static Channel<(IAnimation Animation, IAnimatable Target)> _animationChannel = null!;
    // private static Channel<IAnimationFrame> _frameChannel = null!;
    private static Channel<(IAnimationFrame Frame, IAnimation Source)> _frameChannel = null!;
    // private static ConcurrentDictionary<IAnimatable, IAnimationFrame> _frameDictionary = null!;
    private static ConcurrentDictionary<string, IAnimation> _namedAnimations = new();
    private static IClock _clock = null!;
    private static AsyncCountResetEvent _resetEvent = null!;
    private static int _taskCount;
    private static CancellationTokenSource _cts = null!;
    private static Task[] _computeTasks = [];
    private static readonly object _activityLock = new();
    private static int _activeAnimationCount;
    
    public static int Fps { get; set; } = 60;
    public static double Scale { get; set; } = 1d;

    public static IUIAccessProvider UIAccessProvider { get; private set; } = null!;
    
    private static void _Initialize()
    {
        // 初始化 Channel 与 Dictionary
        _animationChannel = Channel.CreateUnbounded<(IAnimation, IAnimatable)>();
        // _frameChannel = Channel.CreateUnbounded<IAnimationFrame>();
        _frameChannel = Channel.CreateUnbounded<(IAnimationFrame, IAnimation)>();
        
        // 根据核心数量来确定动画计算 Task 数量
        _taskCount = 1;
        Context.Info($"以最多 {_taskCount} 个线程初始化动画计算 Task");

        // 初始化 CancellationTokenSource 与 ResetEvent
        _cts = new CancellationTokenSource();
        _resetEvent = new AsyncCountResetEvent(_taskCount);
        _activeAnimationCount = 0;
        
        // 注册 ValueProcessor
        _RegisterValueProcessors();
        
        // 初始化 UI 线程访问提供器并启动赋值 Task
        UIAccessProvider = new WpfUIAccessProvider(Lifecycle.CurrentApplication.Dispatcher);
        _ = UIAccessProvider.InvokeAsync(async () =>
        {
            if (_cts.IsCancellationRequested) return;
            while (await _frameChannel.Reader.WaitToReadAsync())
            {
                // 读取数据
                while (_frameChannel.Reader.TryRead(out var item))
                {
                    // 如果动画源已被标记取消，直接丢弃该帧，不进行处理
                    if (item.Source.Status == AnimationStatus.Canceled) 
                        continue;

                    try
                    {
                        item.Frame.GetAction()();
                    }
                    catch (Exception ex)
                    {
                        Context.Warn($"应用动画帧失败：{item.Source.GetType().FullName}", ex);
                        try { item.Source.Cancel(); }
                        catch (Exception cancelEx) { Context.Warn("取消异常动画帧时出错", cancelEx); }
                    }
                }
        
                await Task.Yield();
            }
        });

        // 初始化 Clock 并注册 Tick 事件
        _clock = new WinMMClock(Fps);
        _clock.Tick += ClockOnTick;
        
        // 运行动画计算 Task
        _computeTasks = Enumerable.Range(0, _taskCount)
            .Select(_ => Task.Run(_AnimationComputeTaskAsync))
            .ToArray();
    }

    private static void _Uninitialize()
    {
        // 取消动画计算 Task
        lock (_activityLock)
        {
            _cts.Cancel();
            _activeAnimationCount = 0;
            _clock.Stop();
        }

        _animationChannel.Writer.TryComplete();
        _frameChannel.Writer.TryComplete();
        _resetEvent.Set(_taskCount);

        // 停止 Clock 并注销 Tick 事件
        _clock.Tick -= ClockOnTick;

        try
        {
            if (Task.WaitAll(_computeTasks, TimeSpan.FromSeconds(2)))
            {
                _resetEvent.Dispose();
                _cts.Dispose();
            }
            else
            {
                Context.Warn("动画计算 Task 停止超时，跳过同步资源释放");
            }
        }
        catch (AggregateException ex)
        {
            Context.Warn("停止动画计算 Task 时出错", ex.Flatten());
            _resetEvent.Dispose();
            _cts.Dispose();
        }
        
        // 清理 Dictionary
        _namedAnimations.Clear();
    }

    private static void ClockOnTick(object? sender, long e)
    {
        // 通知所有等待的动画计算 Task 进行下一帧计算
        _resetEvent.Set(_taskCount);
    }

    private static async Task _AnimationComputeTaskAsync()
    {
        // 本地动画列表，确保没有一直无法计算的动画
        var animationList = new List<(IAnimation Animation, IAnimatable Target)>(8);
        try
        {
            // 持续监听 Channel 中的动画
            while (!_cts.IsCancellationRequested)
            {
                // 读取所有可用的动画到本地列表
                while (_animationChannel.Reader.TryRead(out var animation))
                {
                    animationList.Add(animation);
                }

                // 如果没有动画，直接等下一帧
                if (animationList.Count == 0)
                {
                    await _resetEvent.WaitAsync();
                    continue;
                }

                for (var i = animationList.Count - 1; i >= 0; i--)
                {
                    // TODO: 支持缓存动画计算结果 (由 AnimationData 支持)
                    var animationEntry = animationList[i];
                    try
                    {
                        if (animationEntry.Animation.Status is AnimationStatus.Canceled or AnimationStatus.Completed)
                        {
                            RemoveAnimation(i, animationEntry.Animation);
                            continue;
                        }

                        var frame = animationEntry.Animation.ComputeNextFrame(animationEntry.Target);
                        if (frame is null) continue;
                        _frameChannel.Writer.TryWrite((frame, animationEntry.Animation));
                        animationEntry.Animation.CurrentFrame++;
                    }
                    catch (Exception ex)
                    {
                        Context.Warn($"动画计算失败：{animationEntry.Animation.GetType().FullName}", ex);
                        try { animationEntry.Animation.Cancel(); }
                        catch (Exception cancelEx) { Context.Warn("取消异常动画时出错", cancelEx); }
                        RemoveAnimation(i, animationEntry.Animation);
                    }
                }

                await _resetEvent.WaitAsync();
            }
        }
        finally
        {
            while (_animationChannel.Reader.TryRead(out var pending))
                animationList.Add(pending);

            var remaining = new HashSet<IAnimation>(ReferenceEqualityComparer.Instance);
            foreach (var entry in animationList)
                remaining.Add(entry.Animation);
            foreach (var animation in remaining)
            {
                try { animation.Cancel(); }
                catch (Exception ex) { Context.Warn("停止动画服务时取消动画失败", ex); }
                try { animation.RaiseCompleted(); }
                catch (Exception ex) { Context.Warn("停止动画服务时触发完成事件失败", ex); }
            }
        }

        void RemoveAnimation(int index, IAnimation animation)
        {
            try { animation.RaiseCompleted(); }
            catch (Exception ex) { Context.Warn("触发动画完成事件时出错", ex); }

            if (!string.IsNullOrEmpty(animation.Name))
            {
                ((ICollection<KeyValuePair<string, IAnimation>>)_namedAnimations)
                    .Remove(new KeyValuePair<string, IAnimation>(animation.Name, animation));
            }

            animationList.RemoveAt(index);
            _OnAnimationFinished();
        }
    }

    private static void _HandleNamedAnimationConflict(IAnimation animation)
    {
        if (string.IsNullOrEmpty(animation.Name)) return;

        _namedAnimations.AddOrUpdate(
            animation.Name, 
            animation, // 如果不存在，直接添加
            (_, existingAnimation) => 
            {
                // 如果已存在同名动画，取消旧动画
                try { existingAnimation.Cancel(); }
                catch (Exception ex) { Context.Warn("取消同名动画时出错", ex); }
                // 替换为新动画
                return animation;
            });
    }

    private static bool _TryQueueAnimation(IAnimation animation, IAnimatable target)
    {
        lock (_activityLock)
        {
            if (_cts.IsCancellationRequested)
                return false;

            try
            {
                if (!_clock.IsRunning)
                    _clock.Start();
            }
            catch (Exception ex)
            {
                Context.Warn("启动动画时钟失败", ex);
                try { animation.Cancel(); }
                catch (Exception cancelEx) { Context.Warn("取消未启动动画时出错", cancelEx); }
                RemoveNamedAnimation(animation);
                return false;
            }

            if (!_animationChannel.Writer.TryWrite((animation, target)))
            {
                if (_activeAnimationCount == 0)
                    _clock.Stop();
                RemoveNamedAnimation(animation);
                return false;
            }

            _activeAnimationCount++;
        }

        return true;
    }

    private static void RemoveNamedAnimation(IAnimation animation)
    {
        if (string.IsNullOrEmpty(animation.Name)) return;
        ((ICollection<KeyValuePair<string, IAnimation>>)_namedAnimations)
            .Remove(new KeyValuePair<string, IAnimation>(animation.Name, animation));
    }

    private static void _OnAnimationFinished()
    {
        lock (_activityLock)
        {
            if (_cts.IsCancellationRequested)
                return;

            if (_activeAnimationCount > 0)
                _activeAnimationCount--;

            if (_activeAnimationCount != 0)
                return;

            _clock.Stop();
            _resetEvent.Reset();
        }
    }
    
    internal static Task PushAnimationAsync(IAnimation animation, IAnimatable target)
    {
        _HandleNamedAnimationConflict(animation);
        
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        
        EventHandler? completedHandler = null;
        completedHandler = (_, _) =>
        {
            animation.Completed -= completedHandler;
            tcs.TrySetResult();
        };
        animation.Completed += completedHandler;
        
        if (!_TryQueueAnimation(animation, target))
        {
            animation.Completed -= completedHandler;
            tcs.TrySetCanceled();
        }
        return tcs.Task;
    }
    
    internal static void PushAnimationFireAndForget(IAnimation animation, IAnimatable target)
    {
        _HandleNamedAnimationConflict(animation);
        
        _TryQueueAnimation(animation, target);
    }
    
    public static void CancelAnimationByName(string name)
    {
        if (_namedAnimations.TryRemove(name, out var animation))
        {
            try { animation.Cancel(); }
            catch (Exception ex) { Context.Warn("按名称取消动画时出错", ex); }
        }
    }
}
