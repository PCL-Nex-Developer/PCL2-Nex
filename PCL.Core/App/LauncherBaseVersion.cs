using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace PCL.Core.App;

/// <summary>
/// Strict launcher/PCL.Core version in <c>yyyy.MM.patch</c> form.
/// </summary>
[JsonConverter(typeof(LauncherBaseVersionJsonConverter))]
public readonly partial record struct LauncherBaseVersion : IComparable<LauncherBaseVersion>
{
    private LauncherBaseVersion(int year, int month, int patch)
    {
        Year = year;
        Month = month;
        Patch = patch;
    }

    public int Year { get; }

    public int Month { get; }

    public int Patch { get; }

    public static LauncherBaseVersion Parse(string value)
    {
        if (!TryParse(value, out var version))
            throw new FormatException($"BaseVersion must use yyyy.MM.patch format: {value}");
        return version;
    }

    public static bool TryParse(string? value, out LauncherBaseVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(value)) return false;

        var match = BaseVersionPattern().Match(value);
        if (!match.Success) return false;

        if (!int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var year) || year <= 0)
            return false;
        if (!int.TryParse(match.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var month))
            return false;
        if (!int.TryParse(match.Groups[3].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var patch))
            return false;

        version = new LauncherBaseVersion(year, month, patch);
        return true;
    }

    public int CompareTo(LauncherBaseVersion other)
    {
        var result = Year.CompareTo(other.Year);
        if (result != 0) return result;
        result = Month.CompareTo(other.Month);
        return result != 0 ? result : Patch.CompareTo(other.Patch);
    }

    public override string ToString() => $"{Year:D4}.{Month:D2}.{Patch}";

    public static bool operator <(LauncherBaseVersion left, LauncherBaseVersion right) => left.CompareTo(right) < 0;
    public static bool operator >(LauncherBaseVersion left, LauncherBaseVersion right) => left.CompareTo(right) > 0;
    public static bool operator <=(LauncherBaseVersion left, LauncherBaseVersion right) => left.CompareTo(right) <= 0;
    public static bool operator >=(LauncherBaseVersion left, LauncherBaseVersion right) => left.CompareTo(right) >= 0;

    [GeneratedRegex(@"^(\d{4})\.(0[1-9]|1[0-2])\.(0|[1-9]\d*)$", RegexOptions.CultureInvariant)]
    private static partial Regex BaseVersionPattern();
}

/// <summary>
/// JSON converter that accepts only the canonical <c>yyyy.MM.patch</c> string representation.
/// </summary>
public sealed class LauncherBaseVersionJsonConverter : JsonConverter<LauncherBaseVersion>
{
    public override LauncherBaseVersion Read(ref Utf8JsonReader reader, Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String ||
            !LauncherBaseVersion.TryParse(reader.GetString(), out var version))
            throw new JsonException("BaseVersion must use strict yyyy.MM.patch format.");

        return version;
    }

    public override void Write(Utf8JsonWriter writer, LauncherBaseVersion value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}
