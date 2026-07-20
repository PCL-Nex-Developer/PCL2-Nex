using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json.Serialization;

namespace PCL.Mixin;

/// <summary>描述一个 PCLX 包中的 Sponge 风格 Mixin 配置。</summary>
public sealed class MixinConfiguration
{
    [JsonPropertyName("required")]
    public bool Required { get; set; } = true;

    [JsonPropertyName("package")]
    public string Package { get; set; } = string.Empty;

    [JsonPropertyName("mixins")]
    public List<string> Mixins { get; set; } = [];

    [JsonPropertyName("priority")]
    public int Priority { get; set; } = 1000;

    [JsonPropertyName("injectors")]
    public MixinInjectorConfiguration Injectors { get; set; } = new();

    /// <summary>可选的 Mixin 专用配置处理器类型全名。</summary>
    [JsonPropertyName("plugin")]
    public string? Plugin { get; set; }
}

public sealed class MixinInjectorConfiguration
{
    [JsonPropertyName("defaultRequire")]
    public int DefaultRequire { get; set; } = 1;
}

/// <summary>
/// Mixin 配置专用处理器。它只参与目标筛选和应用前后校验，不是通用插件生命周期。
/// </summary>
public interface IMixinConfigPlugin
{
    bool ShouldApplyMixin(string targetTypeName, string mixinTypeName) => true;
    void PreApply(MixinApplyContext context) { }
    void PostApply(MixinApplyContext context) { }
}

public sealed record MixinApplyContext(
    string ConfigurationName,
    Assembly SourceAssembly,
    Type MixinType,
    Type TargetType);

public sealed record MixinConflictInfo(
    MethodBase TargetMethod,
    IReadOnlyList<MixinPatchInfo> ApplicationOrder);
