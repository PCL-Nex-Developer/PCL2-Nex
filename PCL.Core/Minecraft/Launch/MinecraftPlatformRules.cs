using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using PCL.Core.Utils.OS;

namespace PCL.Core.Minecraft.Launch;

public static class MinecraftPlatformRules
{
    public static bool CheckCurrent(JsonNode? rules) =>
        Check(rules, EnvironmentInterop.GetCurrentOsName(), RuntimeInformation.OSArchitecture,
            Environment.OSVersion.Version.ToString());

    public static bool Check(JsonNode? rules, string osName, Architecture architecture, string osVersion)
    {
        if (rules is null) return true;

        var required = false;
        foreach (var rule in rules.AsArray())
        {
            var matches = true;
            if (rule?["os"] is JsonNode os)
            {
                if (os["name"] is JsonNode ruleOs)
                    matches &= IsOsMatch(ruleOs.ToString(), osName);
                if (os["version"] is JsonNode ruleVersion)
                    matches &= IsVersionMatch(osVersion, ruleVersion.ToString());
                if (os["arch"] is JsonNode ruleArchitecture)
                    matches &= IsArchitectureMatch(ruleArchitecture.ToString(), architecture);
            }

            if (rule?["features"] is JsonObject features)
            {
                matches &= features["is_demo_user"] is null;
                if (features.Any(prop => prop.Key.Contains("quick_play", StringComparison.Ordinal)))
                    matches = false;
            }

            if (string.Equals(rule?["action"]?.ToString(), "allow", StringComparison.OrdinalIgnoreCase))
            {
                if (matches) required = true;
            }
            else if (matches)
            {
                required = false;
            }
        }

        return required;
    }

    public static bool IsOsMatch(string ruleOsName, string osName) =>
        string.Equals(ruleOsName, "unknown", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(ruleOsName, osName, StringComparison.OrdinalIgnoreCase);

    private static bool IsVersionMatch(string osVersion, string pattern)
    {
        try
        {
            return Regex.IsMatch(osVersion, pattern);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    public static bool IsArchitectureMatch(string ruleArchitecture, Architecture architecture)
    {
        return ruleArchitecture.ToLowerInvariant() switch
        {
            "x86" or "i386" => architecture == Architecture.X86,
            "x86_64" or "x64" or "amd64" => architecture == Architecture.X64,
            "arm" or "arm32" => architecture == Architecture.Arm,
            "arm64" or "aarch64" => architecture == Architecture.Arm64,
            _ => string.Equals(ruleArchitecture, architecture.ToString(), StringComparison.OrdinalIgnoreCase)
        };
    }

    public static string ResolveNativeClassifier(string classifierTemplate, Architecture architecture)
    {
        var architectureBits = architecture == Architecture.X86 || architecture == Architecture.Arm ? "32" : "64";
        return classifierTemplate.Replace("${arch}", architectureBits, StringComparison.Ordinal);
    }
}
