using System.Collections.Generic;

namespace PCL.Plugin.Abstractions;

/// <summary>
/// 插件配置能力。每个插件拥有独立的命名空间，键名自动加上插件 Id 前缀，互不干扰。
/// </summary>
public interface IPluginConfigApi
{
    /// <summary>
    /// 读取一个字符串配置值。
    /// </summary>
    /// <param name="key">配置键（不含插件前缀）</param>
    /// <param name="defaultValue">默认值</param>
    string GetString(string key, string defaultValue = "");

    /// <summary>
    /// 读取一个整型配置值。
    /// </summary>
    int GetInt(string key, int defaultValue = 0);

    /// <summary>
    /// 读取一个布尔配置值。
    /// </summary>
    bool GetBool(string key, bool defaultValue = false);

    /// <summary>
    /// 读取一个双精度浮点配置值。
    /// </summary>
    double GetDouble(string key, double defaultValue = 0d);

    /// <summary>
    /// 写入一个字符串配置值（立即持久化）。
    /// </summary>
    void Set(string key, string value);

    /// <summary>
    /// 写入一个整型配置值（立即持久化）。
    /// </summary>
    void Set(string key, int value);

    /// <summary>
    /// 写入一个布尔配置值（立即持久化）。
    /// </summary>
    void Set(string key, bool value);

    /// <summary>
    /// 写入一个双精度浮点配置值（立即持久化）。
    /// </summary>
    void Set(string key, double value);

    /// <summary>
    /// 判断指定键是否存在。
    /// </summary>
    bool Contains(string key);

    /// <summary>
    /// 删除指定键。
    /// </summary>
    void Remove(string key);

    /// <summary>
    /// 枚举当前插件命名空间下的所有键。
    /// </summary>
    IEnumerable<string> Keys();
}
