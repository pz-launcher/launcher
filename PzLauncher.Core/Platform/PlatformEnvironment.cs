using System.Runtime.Versioning;
using System.Security;
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

    public Version OsVersion { get; } = Environment.OSVersion.Version;

    public long TotalMemoryBytes { get; } = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;

    public StringComparison PathComparison =>
        Os == PlatformKind.Windows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public string? GetEnvironmentVariable(string name) => Environment.GetEnvironmentVariable(name);

    public string? ReadSteamPathFromRegistry() =>
        OperatingSystem.IsWindows() ? ReadSteamPathWindows() : null;

    public IReadOnlyList<string> GetRegistrySubKeyNames(RegistryHiveKind hive, string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            return [];
        }

        using var key = OpenKeyWindows(hive, path);
        return key is null ? [] : WithAccessDenied(key.GetSubKeyNames);
    }

    public string? ReadRegistryString(RegistryHiveKind hive, string path, string name)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        using var key = OpenKeyWindows(hive, path);
        try
        {
            return key?.GetValue(name) as string;
        }
        catch (SecurityException ex)
        {
            throw new UnauthorizedAccessException(ex.Message, ex);
        }
    }

    [SupportedOSPlatform("windows")]
    private static RegistryKey? OpenKeyWindows(RegistryHiveKind hive, string path)
    {
        var registryHive = hive switch
        {
            RegistryHiveKind.LocalMachine => RegistryHive.LocalMachine,
            RegistryHiveKind.CurrentUser => RegistryHive.CurrentUser,
        };

        using var baseKey = RegistryKey.OpenBaseKey(registryHive, RegistryView.Registry64);
        return WithAccessDenied(() => baseKey.OpenSubKey(path));
    }

    // The registry API reports access denied as SecurityException; the interface promises UnauthorizedAccessException.
    private static T WithAccessDenied<T>(Func<T> read)
    {
        try
        {
            return read();
        }
        catch (SecurityException ex)
        {
            throw new UnauthorizedAccessException(ex.Message, ex);
        }
    }

    [SupportedOSPlatform("windows")]
    private string? ReadSteamPathWindows()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            return key?.GetValue("SteamPath") as string;
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            // Treat an unreadable key like a missing one; the locator falls back to default paths.
            LogRegistryUnreadable(logger, ex.Message);
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Steam registry key could not be read; using default Steam paths: {Reason}")]
    private static partial void LogRegistryUnreadable(ILogger logger, string reason);
}
