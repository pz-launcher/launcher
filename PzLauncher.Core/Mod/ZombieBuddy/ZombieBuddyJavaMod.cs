using System.IO.Compression;
using System.Text;
using PzLauncher.Core.Common;

namespace PzLauncher.Core.Mod.ZombieBuddy;

/// <summary>What ZombieBuddy requires of a Java mod it preloads.</summary>
internal static class ZombieBuddyJavaMod
{
    public const string NoJarFile = "no_jar_file";
    public const string MissingJar = "missing_jar";
    public const string MissingPkgName = "missing_pkg_name";
    public const string NotJavaPreload = "not_java_preload";
    public const string NoManifestPreload = "no_manifest_preload";
    public const string UnreadableJar = "unreadable_jar";

    private const string True = "true";

    /// <summary>ZombieBuddy drops a preload entry at startup that fails any of these.</summary>
    public static async Task<Result<ZombieBuddyPreloadEntry>> CheckPreloadableAsync(string modId, ModInfo info, CancellationToken ct)
    {
        if (info.Value(ModFolders.JarFileKey) is not { } jarFile
            || !PathNormalizer.TryNormalizeAbsolute(Path.Combine(info.VersionFolder, jarFile), out var jar))
        {
            return ZombieBuddyErrors.ModNotPreloadable(modId, info.InfPath, NoJarFile);
        }

        if (!File.Exists(jar))
        {
            return ZombieBuddyErrors.ModNotPreloadable(modId, jar, MissingJar);
        }

        if (info.Value(ZombieBuddyFiles.PkgNameKey) is null)
        {
            return ZombieBuddyErrors.ModNotPreloadable(modId, info.InfPath, MissingPkgName);
        }

        if (!string.Equals(info.Value(ZombieBuddyFiles.PreloadKey), True, StringComparison.OrdinalIgnoreCase))
        {
            return ZombieBuddyErrors.ModNotPreloadable(modId, info.InfPath, NotJavaPreload);
        }

        string? manifest;
        try
        {
            manifest = await ReadManifestAsync(jar, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return ZombieBuddyErrors.ModNotPreloadable(modId, jar, UnreadableJar);
        }

        return manifest is not null && HasManifestPreload(manifest)
            ? new ZombieBuddyPreloadEntry(info.InfPath, jar)
            : ZombieBuddyErrors.ModNotPreloadable(modId, jar, NoManifestPreload);
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
        return separator >= 0 && string.Equals(header[..separator], ZombieBuddyFiles.ManifestPreloadAttribute, StringComparison.OrdinalIgnoreCase)
            ? header[(separator + 2)..]
            : null;
    }

    // java.util.jar.JarFile falls back to a case-insensitive match of the manifest name.
    private static async Task<string?> ReadManifestAsync(string jar, CancellationToken ct)
    {
        var archive = await ZipFile.OpenReadAsync(jar, ct).ConfigureAwait(false);
        await using (archive.ConfigureAwait(false))
        {
            var entry = archive.GetEntry(ZombieBuddyFiles.ManifestEntry)
                ?? archive.Entries.FirstOrDefault(e => string.Equals(e.FullName, ZombieBuddyFiles.ManifestEntry, StringComparison.OrdinalIgnoreCase));
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
