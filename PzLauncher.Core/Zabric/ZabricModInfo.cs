using System.IO.Compression;
using System.Text;
using PzLauncher.Core.Common;

namespace PzLauncher.Core.Zabric;

/// <summary>The <c>mod.info</c> ZombieBuddy reads for a version folder, whose jar path is relative to that folder.</summary>
internal sealed class ZabricModInfo(string infPath, string versionFolder, IReadOnlyList<string> lines)
{
    public const string NoJarFile = "no_jar_file";
    public const string MissingJar = "missing_jar";
    public const string MissingPkgName = "missing_pkg_name";
    public const string NotJavaPreload = "not_java_preload";
    public const string NoManifestPreload = "no_manifest_preload";
    public const string UnreadableJar = "unreadable_jar";

    private const string True = "true";

    private readonly IReadOnlyList<string> _lines = lines;

    public string InfPath { get; } = infPath;

    public string VersionFolder { get; } = versionFolder;

    // Zabric's rule: common/mod.info stands in when the version folder's has no javaJarFile.
    /// <summary><c>null</c> when the version folder has no <c>mod.info</c>; throws on an unreadable one.</summary>
    public static async Task<ZabricModInfo?> ReadAsync(string versionFolder, CancellationToken ct)
    {
        var inf = Path.Combine(versionFolder, ZabricFiles.ModInfo);
        if (await ReadLinesAsync(inf, ct).ConfigureAwait(false) is not { } lines)
        {
            return null;
        }

        var own = new ZabricModInfo(inf, versionFolder, lines);
        if (own.Value(ZabricFiles.JarFileKey) is not null || Path.GetDirectoryName(versionFolder) is not { } modFolder)
        {
            return own;
        }

        var commonInf = Path.Combine(modFolder, ZabricFiles.CommonFolder, ZabricFiles.ModInfo);
        var common = await ReadLinesAsync(commonInf, ct).ConfigureAwait(false) is { } commonLines
            ? new ZabricModInfo(commonInf, versionFolder, commonLines)
            : null;
        return common?.Value(ZabricFiles.JarFileKey) is not null ? common : own;
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

    /// <summary>ZombieBuddy drops a preload entry at startup that fails any of these.</summary>
    public async Task<Result<ZabricPreloadEntry>> CheckPreloadableAsync(CancellationToken ct)
    {
        if (Value(ZabricFiles.JarFileKey) is not { } jarFile
            || !PathNormalizer.TryNormalizeAbsolute(Path.Combine(VersionFolder, jarFile), out var jar))
        {
            return ZabricErrors.NotPreloadable(InfPath, NoJarFile);
        }

        if (!File.Exists(jar))
        {
            return ZabricErrors.NotPreloadable(jar, MissingJar);
        }

        if (Value(ZabricFiles.PkgNameKey) is null)
        {
            return ZabricErrors.NotPreloadable(InfPath, MissingPkgName);
        }

        if (!string.Equals(Value(ZabricFiles.PreloadKey), True, StringComparison.OrdinalIgnoreCase))
        {
            return ZabricErrors.NotPreloadable(InfPath, NotJavaPreload);
        }

        string? manifest;
        try
        {
            manifest = await ReadManifestAsync(jar, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return ZabricErrors.NotPreloadable(jar, UnreadableJar);
        }

        return manifest is not null && HasManifestPreload(manifest)
            ? new ZabricPreloadEntry(InfPath, jar)
            : ZabricErrors.NotPreloadable(jar, NoManifestPreload);
    }

    // The main section ends at the first empty line; a line starting with a space continues the previous one.
    // Attribute names are case-insensitive and the last occurrence wins, as in java.util.jar.Manifest.
    internal static bool HasManifestPreload(string manifest)
    {
        string? value = null;
        string? header = null;
        foreach (var line in manifest.ReplaceLineEndings("\n").Split('\n'))
        {
            if (header is not null && line.StartsWith(' '))
            {
                header += line[1..];
                continue;
            }

            value = PreloadValue(header) ?? value;
            header = line.Length == 0 ? null : line;
            if (header is null)
            {
                break;
            }
        }

        value = PreloadValue(header) ?? value;
        return string.Equals(value?.Trim(), True, StringComparison.OrdinalIgnoreCase);
    }

    private static string? PreloadValue(string? header)
    {
        if (header is null)
        {
            return null;
        }

        var separator = header.IndexOf(": ", StringComparison.Ordinal);
        return separator >= 0 && string.Equals(header[..separator], ZabricFiles.ManifestPreloadAttribute, StringComparison.OrdinalIgnoreCase)
            ? header[(separator + 2)..]
            : null;
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

    // java.util.jar.JarFile falls back to a case-insensitive match of the manifest name.
    private static async Task<string?> ReadManifestAsync(string jar, CancellationToken ct)
    {
        var archive = await ZipFile.OpenReadAsync(jar, ct).ConfigureAwait(false);
        await using (archive.ConfigureAwait(false))
        {
            var entry = archive.GetEntry(ZabricFiles.ManifestEntry)
                ?? archive.Entries.FirstOrDefault(e => string.Equals(e.FullName, ZabricFiles.ManifestEntry, StringComparison.OrdinalIgnoreCase));
            if (entry is null)
            {
                return null;
            }

            var stream = await entry.OpenAsync(ct).ConfigureAwait(false);
            await using (stream.ConfigureAwait(false))
            {
                using var reader = new StreamReader(stream, Encoding.UTF8);
                return await reader.ReadToEndAsync(ct).ConfigureAwait(false);
            }
        }
    }
}
