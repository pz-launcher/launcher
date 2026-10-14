using PzLauncher.Core.Platform;

namespace PzLauncher.Core.Tests.TestSupport;

public sealed class FakePlatformEnvironment : IPlatformEnvironment
{
    public PlatformKind Os { get; set; } = PlatformKind.Windows;

    public string HomeDirectory { get; set; } = "";

    public string? SteamRegistryPath { get; set; }

    public Dictionary<string, string> EnvironmentVariables { get; } = new(StringComparer.OrdinalIgnoreCase);

    public StringComparison PathComparison { get; set; } = StringComparison.OrdinalIgnoreCase;

    public string? GetEnvironmentVariable(string name) =>
        EnvironmentVariables.TryGetValue(name, out var value) ? value : null;

    public string? ReadSteamPathFromRegistry() => Os == PlatformKind.Windows ? SteamRegistryPath : null;

    public static FakePlatformEnvironment WithRegistrySteam(string? steamRoot) => new()
    {
        Os = PlatformKind.Windows,
        SteamRegistryPath = steamRoot,
        PathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal,
    };
}
