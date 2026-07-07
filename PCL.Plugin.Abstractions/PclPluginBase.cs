using System.Threading;
using System.Threading.Tasks;

namespace PCL.Plugin.Abstractions;

/// <summary>
/// 插件入口的便捷基类。提供默认的空实现，插件可按需覆写。
/// </summary>
public abstract class PclPluginBase : IPclPlugin
{
    /// <summary>
    /// 加载阶段被注入的上下文，<see cref="LoadAsync"/> 执行后可用。
    /// </summary>
    protected IPluginContext? Context { get; private set; }

    /// <summary>
    /// 由 <see cref="Context"/> 提供的日志记录器，<see cref="LoadAsync"/> 执行后可用。
    /// </summary>
    protected IPluginLogger? Log { get; private set; }

    /// <inheritdoc />
    public virtual Task LoadAsync(IPluginContext context, CancellationToken cancellationToken = default)
    {
        Context = context;
        Log = context.Host.Core.GetLogger(context.Manifest.Name);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public virtual Task UnloadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}
