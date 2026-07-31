using System;
using System.Globalization;
using System.Text.Json.Nodes;

namespace PCL.Core.Minecraft.Java;

public sealed record JavaRuntimeRequirement(int MajorVersion, string? Component)
{
    public Version MinimumVersion => MajorVersion <= 8
        ? new Version(1, MajorVersion, 0, 0)
        : new Version(MajorVersion, 0, 0, 0);

    public static JavaRuntimeRequirement? FromVersionJson(JsonObject? versionJson)
    {
        if (versionJson is null) return null;

        var javaVersion = versionJson["javaVersion"] as JsonObject;
        var majorVersionNode = javaVersion?["majorVersion"] ?? versionJson["java_version"];
        if (!int.TryParse(majorVersionNode?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture,
                out var majorVersion) || majorVersion <= 0)
            return null;

        var component = (javaVersion?["component"] ?? versionJson["java_component"])?.ToString().Trim();
        return new JavaRuntimeRequirement(majorVersion, string.IsNullOrWhiteSpace(component) ? null : component);
    }
}
