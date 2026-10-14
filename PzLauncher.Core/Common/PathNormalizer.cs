using System.Diagnostics.CodeAnalysis;

namespace PzLauncher.Core.Common;

internal static class PathNormalizer
{
    // Throws on invalid input; use TryNormalizeAbsolute for paths from user input, Steam files or the registry.
    public static string Normalize(string path)
    {
        var full = Path.GetFullPath(path);
        var rootLength = Path.GetPathRoot(full.AsSpan()).Length;
        while (full.Length > rootLength && Path.EndsInDirectorySeparator(full))
        {
            full = full[..^1];
        }

        return full;
    }

    // NUL makes GetFullPath throw; a relative path would silently resolve against the working directory.
    public static bool TryNormalizeAbsolute(string? path, [NotNullWhen(true)] out string? normalized)
    {
        normalized = null;
        if (string.IsNullOrWhiteSpace(path)
            || HasInvalidPathChars(path)
            || !Path.IsPathFullyQualified(path))
        {
            return false;
        }

        normalized = Normalize(path);
        return true;
    }

    public static bool HasInvalidPathChars(string path) =>
        path.AsSpan().IndexOfAny(Path.GetInvalidPathChars()) >= 0;
}
