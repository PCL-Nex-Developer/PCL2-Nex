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

    public static bool Equals(string left, string right)
    {
        var normalizedLeft = Path.TrimEndingDirectorySeparator(Path.GetFullPath(left));
        var normalizedRight = Path.TrimEndingDirectorySeparator(Path.GetFullPath(right));
        return string.Equals(normalizedLeft, normalizedRight, Comparison);
    }

    public static string EnsureTrailingSeparator(string path)
    {
        var normalized = Path.GetFullPath(path);
        return Path.EndsInDirectorySeparator(normalized)
            ? normalized
            : normalized + Path.DirectorySeparatorChar;
    }

    public static bool IsWithinDirectory(string candidatePath, string directoryPath, bool allowEqual = false)
    {
        var directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directoryPath));
        var candidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidatePath));
        if (allowEqual && string.Equals(candidate, directory, Comparison)) return true;
        var prefix = Path.EndsInDirectorySeparator(directory)
            ? directory
            : directory + Path.DirectorySeparatorChar;
        return candidate.StartsWith(prefix, Comparison);
    }
}
