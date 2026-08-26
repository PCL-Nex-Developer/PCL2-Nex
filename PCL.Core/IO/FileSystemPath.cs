using System;
using System.IO;

namespace PCL.Core.IO;

public static class FileSystemPath
{
    public static StringComparer Comparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    public static StringComparison Comparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    /// <summary>
    /// Normalizes legacy and alternate path separators before a value is passed to the file system.
    /// </summary>
    public static string NormalizeSeparators(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return path
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);
    }

    /// <summary>
    /// Combines path segments after normalizing separators embedded in legacy segments.
    /// </summary>
    public static string Combine(string path, params string[] paths)
    {
        var result = NormalizeSeparators(path);
        foreach (var segment in paths)
            result = Path.Combine(result, NormalizeSeparators(segment));
        return result;
    }

    public static bool Equals(string left, string right)
    {
        var normalizedLeft = Path.TrimEndingDirectorySeparator(Path.GetFullPath(NormalizeSeparators(left)));
        var normalizedRight = Path.TrimEndingDirectorySeparator(Path.GetFullPath(NormalizeSeparators(right)));
        return string.Equals(normalizedLeft, normalizedRight, Comparison);
    }

    public static string EnsureTrailingSeparator(string path)
    {
        var normalized = Path.GetFullPath(NormalizeSeparators(path));
        return Path.EndsInDirectorySeparator(normalized)
            ? normalized
            : normalized + Path.DirectorySeparatorChar;
    }

    public static bool IsWithinDirectory(string candidatePath, string directoryPath, bool allowEqual = false)
    {
        var directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(NormalizeSeparators(directoryPath)));
        var candidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(NormalizeSeparators(candidatePath)));
        if (allowEqual && string.Equals(candidate, directory, Comparison)) return true;
        var prefix = Path.EndsInDirectorySeparator(directory)
            ? directory
            : directory + Path.DirectorySeparatorChar;
        return candidate.StartsWith(prefix, Comparison);
    }
}
