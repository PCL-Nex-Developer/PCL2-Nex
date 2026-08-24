using PCL.Core.Logging;
using PCL.Core.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace PCL.Core.Minecraft.Java.Parser;
public class PeHeaderParser : IJavaParser
{
    private static readonly Dictionary<string, JavaBrandType> _BrandMap = new()
    {
        ["Eclipse"] = JavaBrandType.EclipseTemurin,
        ["Temurin"] = JavaBrandType.EclipseTemurin,
        ["Bellsoft"] = JavaBrandType.Liberica,
        ["Microsoft"] = JavaBrandType.Microsoft,
        ["Amazon"] = JavaBrandType.Corretto,
        ["Azul"] = JavaBrandType.Zulu,
        ["IBM"] = JavaBrandType.IBMSemeru,
        ["Oracle"] = JavaBrandType.Oracle,
        ["Tencent"] = JavaBrandType.TencentKona,
        ["OpenJDK"] = JavaBrandType.OpenJDK,
        ["Alibaba"] = JavaBrandType.Dragonwell,
        ["GraalVM"] = JavaBrandType.GraalVmCommunity,
        ["JetBrains"] = JavaBrandType.JetBrains
    };

    public JavaInstallation? Parse(string javaExePath)
    {
        try
        {
            if (!File.Exists(javaExePath))
                return null;

            LogWrapper.Info("Java", $"解析 {javaExePath} 的 Java 程序信息");

            var javaFolder = Path.GetDirectoryName(javaExePath)!;
            var isJre = !File.Exists(Path.Combine(javaFolder, JavaPlatform.CompilerName));

            Version fileVersion;
            JavaBrandType brand;
            MachineType arch;
            bool is64Bit;
            if (OperatingSystem.IsWindows())
            {
                var versionInfo = FileVersionInfo.GetVersionInfo(javaExePath);
                fileVersion = Version.Parse(versionInfo.FileVersion ?? "0.0.0.0");
                brand = _DetermineBrand(_NormalizeCompanyName(versionInfo));
                arch = PEHeaderReader.ReadPEHeader(javaExePath).Machine;
                is64Bit = PEHeaderReader.IsMachine64Bit(arch);
            }
            else
            {
                var properties = _ReadJavaProperties(javaExePath);
                fileVersion = ParseVersion(properties);
                brand = _DetermineBrand(properties);
                (arch, is64Bit) = ParseArchitecture(properties);
            }

            return new JavaInstallation(
                javaFolder,
                fileVersion,
                brand,
                arch,
                is64Bit,
                isJre
            );
        }
        catch (Exception ex)
        {
            LogWrapper.Error(ex, $"[Java] 解析 {javaExePath} 时出错");
            return null;
        }
    }

    private static string _NormalizeCompanyName(FileVersionInfo info)
    {
        var name = info.CompanyName ?? info.FileDescription ?? info.ProductName ?? string.Empty;

        // 修复 Oracle/OpenJDK 混淆问题
        if (name.Contains("Oracle", StringComparison.OrdinalIgnoreCase) || name == "N/A")
        {
            if ((info.FileDescription?.Contains("Java(TM)", StringComparison.OrdinalIgnoreCase) ?? false) ||
                (info.ProductName?.Contains("Java(TM)", StringComparison.OrdinalIgnoreCase) ?? false))
                return "Oracle";
            return "OpenJDK";
        }
        return name;
    }

    private static JavaBrandType _DetermineBrand(string output)
    {
        var match = _BrandMap.Keys
            .FirstOrDefault(k => output.Contains(k, StringComparison.OrdinalIgnoreCase));
        return match is not null ? _BrandMap[match] : JavaBrandType.Unknown;
    }

    private static string _ReadJavaProperties(string javaExePath)
    {
        var psi = new ProcessStartInfo(javaExePath)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("-XshowSettings:properties");
        psi.ArgumentList.Add("-version");

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("无法启动 Java 进程");
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(5000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("读取 Java 运行时信息超时");
        }
        return standardOutput.GetAwaiter().GetResult() + Environment.NewLine + standardError.GetAwaiter().GetResult();
    }

    internal static Version ParseVersion(string output)
    {
        var property = Regex.Match(output, @"(?m)^\s*java\.version\s*=\s*(?<value>\S+)");
        var source = property.Success ? property.Groups["value"].Value : output;
        var match = Regex.Match(source, @"\d+(?:[._]\d+){0,3}");
        if (!match.Success) throw new FormatException("Java 未返回可识别的版本号");
        var parts = match.Value.Replace('_', '.').Split('.').Take(4).ToList();
        while (parts.Count < 2) parts.Add("0");
        return Version.Parse(string.Join('.', parts));
    }

    internal static (MachineType Architecture, bool Is64Bit) ParseArchitecture(string output)
    {
        var match = Regex.Match(output, @"(?m)^\s*os\.arch\s*=\s*(?<value>\S+)");
        var value = match.Success ? match.Groups["value"].Value.ToLowerInvariant() : string.Empty;
        return value switch
        {
            "amd64" or "x86_64" => (MachineType.AMD64, true),
            "aarch64" or "arm64" => (MachineType.ARM64, true),
            "x86" or "i386" or "i686" => (MachineType.I386, false),
            "arm" or "arm32" => (MachineType.ARM, false),
            _ => (MachineType.Unknown, value.Contains("64", StringComparison.Ordinal))
        };
    }
}
