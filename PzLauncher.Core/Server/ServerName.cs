namespace PzLauncher.Core.Server;

internal static class ServerName
{
    private static readonly char[] Forbidden = ['\\', '/', ':', '*', '?', '"', '<', '>', '|'];

    // Becomes file names on every OS, and PZ adds suffixes such as _SandboxVars.
    public static bool IsValidNew(string name) =>
        name.Length is >= 1 and <= 64 && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-');

    /// <summary>An existing server's name, which PZ or the user may have chosen outside <see cref="IsValidNew"/>: only a plain file name.</summary>
    public static bool IsFileName(string name) =>
        !string.IsNullOrWhiteSpace(name)
        && name is not ("." or "..")
        && name.IndexOfAny(Forbidden) < 0
        && !name.Any(char.IsControl);
}
