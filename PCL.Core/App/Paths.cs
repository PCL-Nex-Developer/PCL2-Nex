using System;
using System.IO;
using PCL.Core.Utils.OS;
using Special = System.Environment.SpecialFolder;

namespace PCL.Core.App;

/// <summary>
/// Application global paths.<br/>
/// <b>NOTE</b>: The behaviors of all path strings depends on <see cref="Path"/> API provided by
/// .NET standard library. You should use <see cref="Path"/> and other APIs relative to it to process
/// any path string from this service, rather than concat paths manually.
/// </summary>
public static class Paths
{
    /// <summary>
    /// The default directory used for relative path combining.
    /// </summary>
    public static string DefaultDirectory => AppContext.BaseDirectory;

    private static string _data;
    private static string _sharedData;
    private static string _sharedLocalData;
    private static string _temp;
    
    /// <summary>
    /// Per-instance data directory.
    /// </summary>
    public static string Data { get => _data; set => _data = value; }

    /// <summary>
    /// Shared synchronized data directory.
    /// </summary>
    public static string SharedData { get => _sharedData; set => _sharedData = value; }
    
    /// <summary>
    /// Shared synchronized data directory of old versions.<br/>
    /// Keep the value just for migration, DO NOT USE IT.
    /// </summary>
    public static string OldSharedData { get; set; }

    /// <summary>
    /// Shared local data directory, used to put some large files that can be released or downloaded back anytime.
    /// </summary>
    public static string SharedLocalData { get => _sharedLocalData; set => _sharedLocalData = value; }
    
    /// <summary>
    /// Temporary files directory (can be deleted anytime, except when the program is running).
    /// </summary>
    public static string Temp { get => _temp; set => _temp = value; }

    /// <summary>
    /// 插件目录（位于程序运行目录下的 <c>PCL/Plugins</c>）。第三方插件应按 <c>Plugins/&lt;插件 ID&gt;</c> 结构放置。
    /// </summary>
    public static string Plugins { get; set; } = string.Empty;

    /// <summary>已安装插件的结构化目录，每个插件一个子目录。</summary>
    public static string PluginInstalled { get; set; } = string.Empty;

    /// <summary>仓库信任与安装记录目录。</summary>
    public static string PluginTrust { get; set; } = string.Empty;

    /// <summary>下载与解压临时目录。</summary>
    public static string PluginTemp { get; set; } = string.Empty;

    /// <summary>
    /// Get path string relative to a special folder.
    /// </summary>
    /// <param name="folder">the special folder</param>
    /// <param name="relative">the relative path</param>
    /// <returns>the path string relative to the special folder</returns>
    public static string GetSpecialPath(Special folder, string relative)
    {
        var folderPath = Environment.GetFolderPath(folder);
        return Path.Combine(folderPath, relative);
    }

    static Paths()
    {
#if DEBUG
        const string name = "PCLNex_Debug";
        const string oldName = ".PCLNexDebug";
#else
        const string name = "PCLNex";
        const string oldName = ".PCLNex";
#endif
        // fill paths
        _data = Path.Combine(DefaultDirectory, "PCL");
        _sharedData = GetSpecialPath(Special.ApplicationData, name);
        _sharedLocalData = GetSpecialPath(Special.LocalApplicationData, name);
        _temp = Path.Combine(Path.GetTempPath(), name);
        OldSharedData = GetSpecialPath(Special.ApplicationData, oldName);
#if DEBUG
        // read environment variables
        EnvironmentInterop.ReadVariable("PCL_PATH", ref _data);
        EnvironmentInterop.ReadVariable("PCL_PATH_SHARED", ref _sharedData);
        EnvironmentInterop.ReadVariable("PCL_PATH_LOCAL", ref _sharedLocalData);
        EnvironmentInterop.ReadVariable("PCL_PATH_TEMP", ref _temp);
#endif
        Plugins = Path.Combine(_data, "Plugins");
        PluginInstalled = Plugins;
        PluginTrust = Path.Combine(_sharedLocalData, "Plugins", "trust");
        PluginTemp = Path.Combine(_sharedLocalData, "Plugins", "temp");
        // create directories
        Directory.CreateDirectory(_data);
        Directory.CreateDirectory(_sharedData);
        Directory.CreateDirectory(_sharedLocalData);
        Directory.CreateDirectory(_temp);
        Directory.CreateDirectory(Plugins);
        Directory.CreateDirectory(PluginInstalled);
        Directory.CreateDirectory(PluginTrust);
        Directory.CreateDirectory(PluginTemp);
    }
}
