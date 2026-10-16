namespace PzLauncher.Core.Platform;

/// <summary>The only way Core reads OS-level facts, so tests can fake them.</summary>
public interface IPlatformEnvironment
{
    PlatformKind Os { get; }

    string HomeDirectory { get; }

    /// <summary>The running OS's version number, e.g. <c>10.0.26100.0</c> on Windows 11.</summary>
    Version OsVersion { get; }

    long TotalMemoryBytes { get; }

    string? GetEnvironmentVariable(string name);

    /// <summary><c>HKCU\Software\Valve\Steam\SteamPath</c>; <c>null</c> off Windows.</summary>
    string? ReadSteamPathFromRegistry();

    /// <summary>64-bit view; empty off Windows or for a missing key. Throws <see cref="IOException"/> or <see cref="UnauthorizedAccessException"/> when an existing key can't be read.</summary>
    IReadOnlyList<string> GetRegistrySubKeyNames(RegistryHiveKind hive, string path);

    /// <summary>Like <see cref="GetRegistrySubKeyNames"/>; <c>null</c> for a missing or non-string value.</summary>
    string? ReadRegistryString(RegistryHiveKind hive, string path, string name);

    StringComparison PathComparison { get; }
}
