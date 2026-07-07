using System;

namespace PCL.Plugin.Abstractions;

/// <summary>
/// 命令行子命令注册能力。<br/>
/// 允许插件注册形如 <c>PCL.exe &lt;command&gt; [args]</c> 的自定义子命令。
/// </summary>
public interface ICliCommandRegistrar
{
    /// <summary>
    /// 注册一个子命令。
    /// </summary>
    /// <param name="descriptor">命令描述符</param>
    /// <returns>用于注销的 <see cref="IDisposable"/></returns>
    IDisposable RegisterCommand(CliCommandDescriptor descriptor);
}

/// <summary>
/// 子命令处理委托。
/// </summary>
/// <param name="args">剩余参数</param>
/// <returns>退出码</returns>
public delegate int CliCommandHandler(string[] args);

/// <summary>
/// 命令描述符。
/// </summary>
public sealed class CliCommandDescriptor
{
    /// <summary>命令名（不含前缀，必须由小写字母、数字、连字符组成）。</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>简短描述。</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>用法说明。</summary>
    public string Usage { get; init; } = string.Empty;

    /// <summary>处理委托。</summary>
    public required CliCommandHandler Handler { get; init; }
}
