using System.Globalization;
using System.Text.Json;
using PzLauncher.Core.Common;
using PzLauncher.Core.Platform;

namespace PzLauncher.Core.Launch;

/// <param name="Windows">OS version key as written (e.g. <c>6.1</c>, <c>10.0.17134</c>, <c>7</c>) → extra JVM args.</param>
internal sealed record PzLaunchConfig(
    string MainClass,
    IReadOnlyList<string> Classpath,
    IReadOnlyList<string> VmArgs,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Windows)
{
    /// <summary>The json's <c>vmArgs</c> plus, on Windows, the entry for <paramref name="osVersion"/>.</summary>
    public IReadOnlyList<string> JvmArgs(PlatformKind os, Version osVersion) =>
        os == PlatformKind.Windows ? [.. VmArgs, .. WindowsVmArgs(osVersion)] : VmArgs;

    // Not documented by the game; inferred from the shipped files (game "6.1"/"10.0.17134", server "7"/"10"):
    // only the highest key not above the OS applies. Applying every lower key would pass two GC flags, which the JVM rejects.
    public IReadOnlyList<string> WindowsVmArgs(Version osVersion)
    {
        IReadOnlyList<string> best = [];
        Version? bestVersion = null;
        foreach (var (key, args) in Windows)
        {
            if (ParseOsVersion(key) is { } version && version <= osVersion && (bestVersion is null || version > bestVersion))
            {
                bestVersion = version;
                best = args;
            }
        }

        return best;
    }

    public static async Task<Result<PzLaunchConfig>> ReadAsync(string path, CancellationToken ct)
    {
        var json = await PzLaunchConfigFile.ReadAsync(path, ct).ConfigureAwait(false);
        return json.IsSuccess ? Parse(json.Value, path) : json.Error;
    }

    public static Result<PzLaunchConfig> Parse(string json, string path)
    {
        PzLaunchConfigJson? raw;
        try
        {
            raw = JsonSerializer.Deserialize(json, PzLaunchConfigJsonContext.Default.PzLaunchConfigJson);
        }
        catch (JsonException ex)
        {
            return LaunchErrors.ConfigMalformed(path, ex.Message);
        }

        if (raw is null)
        {
            return LaunchErrors.ConfigMalformed(path, "The file holds no JSON object.");
        }

        var classpath = NonEmpty(raw.Classpath);
        if (string.IsNullOrWhiteSpace(raw.MainClass) || classpath.Count == 0)
        {
            return LaunchErrors.ConfigMalformed(path, "mainClass or classpath is missing.");
        }

        var windows = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var (key, entry) in raw.Windows ?? new Dictionary<string, PzLaunchOsEntryJson?>())
        {
            windows[key] = NonEmpty(entry?.VmArgs);
        }

        return new PzLaunchConfig(raw.MainClass, classpath, NonEmpty(raw.VmArgs), windows);
    }

    // Version.TryParse needs at least "major.minor"; the server file uses bare "7" / "10".
    private static Version? ParseOsVersion(string key) =>
        Version.TryParse(key, out var version) ? version
        : int.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out var major) ? new Version(major, 0)
        : null;

    private static List<string> NonEmpty(List<string?>? values) =>
        values is null ? [] : [.. values.OfType<string>().Where(v => !string.IsNullOrWhiteSpace(v))];
}
