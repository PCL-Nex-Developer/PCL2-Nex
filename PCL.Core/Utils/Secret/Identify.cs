using System;
using System.Text;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using PCL.Core.Logging;
using PCL.Core.Utils.Exts;
using PCL.Core.Utils.Hash;

namespace PCL.Core.Utils.Secret;

public class Identify
{
    private static readonly Lazy<byte[]> _rawId = new(_GetRawId);
    private static readonly Lazy<string> _launcherId = new(_GetLauncherId);

    public static byte[] RawId => _rawId.Value;
    public static string LauncherId => _launcherId.Value;

    private static byte[] _GetRawId()
    {
        var code = new StringBuilder();
        try
        {
            code.Append(GetMachineIdentity());
        }
        catch (Exception ex)
        {
            LogWrapper.Error(ex, "Identify", "获取设备基础信息失败");
        }

        return Encoding.UTF8.GetBytes(SHA512Provider.Instance.ComputeHash(code.ToString()).ToHexString());
    }

    private static string GetMachineIdentity()
    {
        if (OperatingSystem.IsWindows())
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
            var machineGuid = key?.GetValue("MachineGuid")?.ToString()?.Trim();
            if (!string.IsNullOrWhiteSpace(machineGuid)) return "MachineGuid:" + machineGuid;
        }
        else
        {
            foreach (var path in new[] { "/etc/machine-id", "/var/lib/dbus/machine-id", "/etc/hostid" })
            {
                if (!File.Exists(path)) continue;
                var machineId = File.ReadAllText(path).Trim();
                if (!string.IsNullOrWhiteSpace(machineId)) return "MachineId:" + machineId;
            }
        }

        return $"MachineName:{Environment.MachineName}|OS:{RuntimeInformation.OSDescription}|Arch:{RuntimeInformation.OSArchitecture}";
    }

    private static string _GetLauncherId()
    {
        try
        {
            var prefix = "PCL-Nex|"u8.ToArray();
            var ctx = RawId;
            var suffix = "|LauncherId"u8.ToArray();

            var buffer = new byte[prefix.Length + ctx.Length + suffix.Length];
            var bufferSpan = buffer.AsSpan();
            prefix.CopyTo(bufferSpan[..prefix.Length]);
            ctx.CopyTo(bufferSpan.Slice(prefix.Length, ctx.Length));
            suffix.CopyTo(bufferSpan[(prefix.Length + ctx.Length)..]);

            var sample = SHA512Provider.Instance.ComputeHash(bufferSpan).ToHexString();
            bufferSpan.Clear();

            // 16 in length, 8 bytes, 64 bits, enough for us
            return sample.Substring(64, 16)
                .ToUpper()
                .Insert(4, "-")
                .Insert(9, "-")
                .Insert(14, "-");
        }
        catch (Exception ex)
        {
            LogWrapper.Error(ex, "Identify", "无法获取识别码");
            return "PCL-Nex-CE-GOOD-2025";
        }
    }
}
