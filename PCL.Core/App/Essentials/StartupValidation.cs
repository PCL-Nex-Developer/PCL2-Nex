using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace PCL.Core.App.Essentials;

public static class StartupValidation
{
    /// <summary>
    ///     确保 WPF 字体渲染环境正常（修复缺失 %windir% 环境变量导致的字体渲染异常 #3555）
    /// </summary>
    public static void EnsureWpfFont()
    {
        // Fonts 的静态构造函数只会执行一次。若先访问 Fonts 再修复 windir，
        // UriFormatException 会被包装成 TypeInitializationException，并使该类型在整个进程中永久不可用。
        // 因此必须在第一次触碰 WPF 字体 API 之前修复当前进程环境变量。
        EnsureWindowsDirectoryEnvironment();
        _ = new FormattedText("", CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            Fonts.SystemTypefaces.First(), 96d, Brushes.Black, 96d);
    }

    internal static string EnsureWindowsDirectoryEnvironment()
    {
        var windowsDirectory = Environment.GetEnvironmentVariable("windir", EnvironmentVariableTarget.Process);
        if (IsUsableWindowsDirectory(windowsDirectory)) return windowsDirectory!;

        windowsDirectory = Environment.GetEnvironmentVariable("SystemRoot", EnvironmentVariableTarget.Process);
        if (!IsUsableWindowsDirectory(windowsDirectory))
            windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

        if (!IsUsableWindowsDirectory(windowsDirectory))
            throw new InvalidOperationException("无法确定 Windows 系统目录，WPF 字体系统无法初始化。");

        Environment.SetEnvironmentVariable("windir", windowsDirectory, EnvironmentVariableTarget.Process);
        return windowsDirectory!;
    }

    private static bool IsUsableWindowsDirectory(string? path)
    {
        return !string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path) && Directory.Exists(path);
    }
}
