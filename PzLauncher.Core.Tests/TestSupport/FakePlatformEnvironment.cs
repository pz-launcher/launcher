using PzLauncher.Core.Platform;

namespace PzLauncher.Core.Tests.TestSupport;

public sealed class FakePlatformEnvironment : IPlatformEnvironment
{
    public PlatformKind Os { get; set; } = PlatformKind.Windows;

    public string HomeDirectory { get; set; } = "";

    public Version OsVersion { get; set; } = new(10, 0, 26100, 0);

    public long TotalMemoryBytes { get; set; } = 16L * 1024 * 1024 * 1024;

    public string? SteamRegistryPath { get; set; }

    public Dictionary<string, string> EnvironmentVariables { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Key path → value name → value, per hive. Subkeys are derived from the key paths.</summary>
    public Dictionary<(RegistryHiveKind Hive, string Path), Dictionary<string, string>> RegistryKeys { get; } =
        new(RegistryKeyComparer.Instance);

    /// <summary>Reading these keys (or their subkey list) throws <see cref="UnauthorizedAccessException"/>.</summary>
    public HashSet<(RegistryHiveKind Hive, string Path)> UnreadableRegistryKeys { get; } = new(RegistryKeyComparer.Instance);

    public StringComparison PathComparison { get; set; } = StringComparison.OrdinalIgnoreCase;

    public string? GetEnvironmentVariable(string name) =>
        EnvironmentVariables.TryGetValue(name, out var value) ? value : null;

    public string? ReadSteamPathFromRegistry() => Os == PlatformKind.Windows ? SteamRegistryPath : null;

    public void SetRegistryValue(RegistryHiveKind hive, string path, string name, string value)
    {
        if (!RegistryKeys.TryGetValue((hive, path), out var values))
        {
            values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            RegistryKeys[(hive, path)] = values;
        }

        values[name] = value;
    }

    public IReadOnlyList<string> GetRegistrySubKeyNames(RegistryHiveKind hive, string path)
    {
        if (Os != PlatformKind.Windows)
        {
            return [];
        }

        ThrowIfUnreadable(hive, path);
        var prefix = path + @"\";
        return
        [
            .. RegistryKeys.Keys
                .Where(key => key.Hive == hive && key.Path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .Select(key => key.Path[prefix.Length..].Split('\\')[0])
                .Distinct(StringComparer.OrdinalIgnoreCase),
        ];
    }

    public string? ReadRegistryString(RegistryHiveKind hive, string path, string name)
    {
        if (Os != PlatformKind.Windows)
        {
            return null;
        }

        ThrowIfUnreadable(hive, path);
        return RegistryKeys.TryGetValue((hive, path), out var values) && values.TryGetValue(name, out var value) ? value : null;
    }

    public static FakePlatformEnvironment WithRegistrySteam(string? steamRoot) => new()
    {
        Os = PlatformKind.Windows,
        SteamRegistryPath = steamRoot,
        PathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal,
    };

    private void ThrowIfUnreadable(RegistryHiveKind hive, string path)
    {
        if (UnreadableRegistryKeys.Contains((hive, path)))
        {
            throw new UnauthorizedAccessException($"Access denied: {path}");
        }
    }

    private sealed class RegistryKeyComparer : IEqualityComparer<(RegistryHiveKind Hive, string Path)>
    {
        public static readonly RegistryKeyComparer Instance = new();

        public bool Equals((RegistryHiveKind Hive, string Path) x, (RegistryHiveKind Hive, string Path) y) =>
            x.Hive == y.Hive && string.Equals(x.Path, y.Path, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((RegistryHiveKind Hive, string Path) obj) =>
            HashCode.Combine(obj.Hive, StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Path));
    }
}
