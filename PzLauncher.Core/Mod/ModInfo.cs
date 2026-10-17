using System.Text;

namespace PzLauncher.Core.Mod;

/// <summary>The <c>mod.info</c> read for a version folder, whose jar path is relative to that folder.</summary>
internal sealed class ModInfo(string infPath, string versionFolder, IReadOnlyList<string> lines)
{
    private readonly IReadOnlyList<string> _lines = lines;

    public string InfPath { get; } = infPath;

    public string VersionFolder { get; } = versionFolder;

    // As ZombieBuddy reads it: common/mod.info stands in when the version folder's has no javaJarFile.
    /// <summary><c>null</c> when the version folder has no <c>mod.info</c>; throws on an unreadable one.</summary>
    public static async Task<ModInfo?> ReadAsync(string versionFolder, CancellationToken ct)
    {
        var inf = Path.Combine(versionFolder, ModFolders.ModInfoFile);
        if (await ReadLinesAsync(inf, ct).ConfigureAwait(false) is not { } lines)
        {
            return null;
        }

        var own = new ModInfo(inf, versionFolder, lines);
        if (own.Value(ModFolders.JarFileKey) is not null || Path.GetDirectoryName(versionFolder) is not { } modFolder)
        {
            return own;
        }

        var commonInf = Path.Combine(modFolder, ModFolders.CommonFolder, ModFolders.ModInfoFile);
        var common = await ReadLinesAsync(commonInf, ct).ConfigureAwait(false) is { } commonLines
            ? new ModInfo(commonInf, versionFolder, commonLines)
            : null;
        return common?.Value(ModFolders.JarFileKey) is not null ? common : own;
    }

    // As ZombieBuddy reads mod.info: the first line with this key, in any case, and a non-empty value.
    public string? Value(string key)
    {
        var prefix = key + "=";
        foreach (var line in _lines)
        {
            if (line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && line[prefix.Length..].Trim() is { Length: > 0 } value)
            {
                return value;
            }
        }

        return null;
    }

    private static async Task<string[]?> ReadLinesAsync(string path, CancellationToken ct)
    {
        try
        {
            return await File.ReadAllLinesAsync(path, Encoding.UTF8, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
    }
}
