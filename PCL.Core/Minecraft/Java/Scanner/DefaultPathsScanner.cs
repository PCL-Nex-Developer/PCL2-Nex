using PCL.Core.App;
using PCL.Core.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PCL.Core.Minecraft.Java.Scanner;

public class DefaultPathsScanner : IJavaScanner
{
    private const int MaxSearchDepth = 6;

    public void Scan(ICollection<string> results)
    {
        try
        {
            var searchRoots = _GetSearchRoots();
            LogWrapper.Info($"[Java] 对下列目录进行广度关键词搜索:{Environment.NewLine}{string.Join(Environment.NewLine, searchRoots)}");

            foreach (var root in searchRoots)
            {
                _BfsSearch(root, results);
            }
        }
        catch (Exception ex)
        {
            LogWrapper.Error(ex, "Java", "默认路径扫描失败");
        }
    }

    private static HashSet<string> _GetSearchRoots()
    {
        var roots = new HashSet<string>(JavaPlatform.PathComparer);
        AddIfNotEmpty(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft", "runtime"));
        AddIfNotEmpty(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        AddIfNotEmpty(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        AddIfNotEmpty(Paths.Data);

        if (OperatingSystem.IsWindows())
        {
            var keyFolders = new[] { "Program Files", "Program Files (x86)" };
            var drives = DriveInfo.GetDrives()
                .Where(d => d.DriveType.Equals(DriveType.Fixed) && d.IsReady)
                .Select(d => d.Name);

            foreach (var drive in drives)
            {
                foreach (var folder in keyFolders)
                {
                    roots.Add(Path.Combine(drive, folder));
                }

                // 根目录关键词搜索
                try
                {
                    var rootDirs = Directory.EnumerateDirectories(drive)
                        .Where(dir => JavaConsts.MostPossibleKeywords.Any(k =>
                            Path.GetFileName(dir).Contains(k, StringComparison.OrdinalIgnoreCase)));

                    foreach (var dir in rootDirs)
                        roots.Add(dir);
                }
                catch (UnauthorizedAccessException) { /* 忽略无权限目录 */ }
                catch (IOException) { /* 忽略IO错误 */ }
            }
        }
        else
        {
            foreach (var root in JavaPlatform.GetPlatformSearchRoots()) AddIfNotEmpty(root);
        }

        return roots;

        void AddIfNotEmpty(string path)
        {
            if (!string.IsNullOrWhiteSpace(path)) roots.Add(path);
        }
    }

    private static void _BfsSearch(string rootPath, ICollection<string> results)
    {
        if (!Directory.Exists(rootPath)) return;

        var queue = new Queue<(string Path, int Depth)>();
        queue.Enqueue((rootPath, 0));

        while (queue.Count > 0)
        {
            var (current, depth) = queue.Dequeue();
            if (depth > MaxSearchDepth || !Directory.Exists(current)) continue;

            try
            {
                foreach (var subDir in Directory.EnumerateDirectories(current))
                {
                    var javaExe = Path.Combine(subDir, JavaPlatform.ExecutableName);
                    if (File.Exists(javaExe))
                    {
                        results.Add(javaExe);
                        continue;
                    }

                    if (_ShouldExploreDeeper(subDir))
                        queue.Enqueue((subDir, depth + 1));
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or DirectoryNotFoundException)
            {
                LogWrapper.Debug($"跳过目录 {current}: {ex.Message}");
            }
            catch (Exception ex)
            {
                LogWrapper.Error(ex, "Java", $"搜索目录 {current} 时出错");
            }
        }
    }

    private static bool _ShouldExploreDeeper(string path)
    {
        var name = Path.GetFileName(path).AsSpan();

        foreach (var ex in JavaConsts.ExcludeFolderNames)
            if (name.Contains(ex, StringComparison.OrdinalIgnoreCase))
                return false;

        foreach (var kw in JavaConsts.AllKeywords)
            if (name.Contains(kw, StringComparison.OrdinalIgnoreCase))
                return true;

        return _IsVersionLikeDirectory(name);
    }

    private static bool _IsVersionLikeDirectory(ReadOnlySpan<char> name)
    {
        if (name.IsEmpty || name.Length > 20)
            return false;

        var hasDigit = false;
        foreach (var c in name)
        {
            if (char.IsDigit(c))
            {
                hasDigit = true;
            }
            else if (c != '.' && c != '_' && c != '-')
            {
                return false;
            }
        }
        return hasDigit;
    }
}
