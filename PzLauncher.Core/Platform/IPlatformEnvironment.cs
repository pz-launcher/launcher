namespace PzLauncher.Core.Platform;

/// <summary>The only way Core reads OS-level facts, so tests can fake them.</summary>
public interface IPlatformEnvironment
{
    PlatformKind Os { get; }

    string HomeDirectory { get; }

    string? GetEnvironmentVariable(string name);

    /// <summary><c>HKCU\Software\Valve\Steam\SteamPath</c>; <c>null</c> off Windows.</summary>
    string? ReadSteamPathFromRegistry();

    StringComparison PathComparison { get; }
}
