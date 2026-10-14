using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace PzLauncher.Core.Platform;

internal sealed partial class PlatformEnvironment(ILogger<PlatformEnvironment> logger) : IPlatformEnvironment
{
    public PlatformKind Os { get; } =
        OperatingSystem.IsWindows() ? PlatformKind.Windows
        : OperatingSystem.IsMacOS() ? PlatformKind.MacOS
        : PlatformKind.Linux;

    public string HomeDirectory { get; } =
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public StringComparison PathComparison =>
        Os == PlatformKind.Windows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public string? GetEnvironmentVariable(string name) => Environment.GetEnvironmentVariable(name);

    public string? ReadSteamPathFromRegistry() =>
        OperatingSystem.IsWindows() ? ReadSteamPathWindows() : null;

    [SupportedOSPlatform("windows")]
    private string? ReadSteamPathWindows()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            return key?.GetValue("SteamPath") as string;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            // Treat an unreadable key like a missing one; the locator falls back to default paths.
            LogRegistryUnreadable(logger, ex.Message);
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Steam registry key could not be read; using default Steam paths: {Reason}")]
    private static partial void LogRegistryUnreadable(ILogger logger, string reason);
}
