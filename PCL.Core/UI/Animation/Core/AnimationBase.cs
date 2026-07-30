using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using PCL.Core.UI.Animation.Animatable;

namespace PCL.Core.UI.Animation.Core;

public abstract class AnimationBase : DependencyObject, IAnimation
{
    public string Name { get; set; } = string.Empty;
    private volatile int _status = (int)AnimationStatus.NotStarted;
    public AnimationStatus Status
    {
        get => (AnimationStatus)_status;
        internal set => Interlocked.Exchange(ref _status, (int)value);
    }
    public abstract int CurrentFrame { get; set; }
    
    public abstract Task<IAnimation> RunAsync(IAnimatable target);
    public abstract IAnimation RunFireAndForget(IAnimatable target);
    public abstract void Cancel();
    public abstract IAnimationFrame? ComputeNextFrame(IAnimatable target);
    
    public void RaiseStarted() => RaiseHandlers(Started);
    public void RaiseCompleted() => RaiseHandlers(Completed);

    private void RaiseHandlers(EventHandler? handlers)
    {
        if (handlers is null) return;

        List<Exception>? errors = null;
        foreach (EventHandler handler in handlers.GetInvocationList())
        {
            try { handler(this, EventArgs.Empty); }
            catch (Exception ex) { (errors ??= []).Add(ex); }
        }

        if (errors is not null)
            throw new AggregateException(errors);
    }
    
    public event EventHandler? Started;
    public event EventHandler? Completed;
}
