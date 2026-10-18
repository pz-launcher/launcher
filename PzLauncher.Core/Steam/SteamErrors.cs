using System.Globalization;
using PzLauncher.Core.Common;
using PzLauncher.Core.Platform;

namespace PzLauncher.Core.Steam;

internal static class SteamErrors
{
    public static Error NotInstalled() =>
        new(ErrorCode.SteamNotInstalled, "No Steam installation was found.", ErrorKind.NotFound);

    public static Error LibraryUnreadable(string path, string reason) =>
        new(ErrorCode.SteamLibraryUnreadable, $"Steam library file could not be read: {path} ({reason})", ErrorKind.External)
        {
            Details = new Dictionary<string, string> { ["path"] = path, ["reason"] = reason },
        };

    public static Error ManifestUnreadable(string path, string reason) =>
        new(ErrorCode.SteamManifestUnreadable, $"Steam app manifest could not be read: {path} ({reason})", ErrorKind.External)
        {
            Details = new Dictionary<string, string> { ["path"] = path, ["reason"] = reason },
        };

    public static Error VdfMalformed(string reason) =>
        new(ErrorCode.SteamVdfMalformed, $"Malformed VDF: {reason}", ErrorKind.Validation)
        {
            Details = new Dictionary<string, string> { ["reason"] = reason },
        };

    public static Error PlatformUnsupported(PlatformKind os) =>
        new(ErrorCode.SteamPlatformUnsupported, $"Steam launch options are only managed on Windows, not {os}.", ErrorKind.Validation);

    public static Error NoCurrentAccount(string path, string reason) =>
        new(ErrorCode.SteamNoCurrentAccount, $"No current Steam account in {path} ({reason})", ErrorKind.NotFound)
        {
            Details = new Dictionary<string, string> { ["path"] = path, ["reason"] = reason },
        };

    public static Error AppNotInLocalConfig(string path, int appId) =>
        new(ErrorCode.SteamAppNotInLocalConfig, $"App {appId} has no entry in {path}", ErrorKind.NotFound)
        {
            Details = new Dictionary<string, string> { ["path"] = path, ["appId"] = appId.ToString(CultureInfo.InvariantCulture) },
        };

    public static Error LocalConfigMalformed(string path, string reason) =>
        new(ErrorCode.SteamLocalConfigMalformed, $"Steam local config can't be edited safely: {path} ({reason})", ErrorKind.External)
        {
            Details = new Dictionary<string, string> { ["path"] = path, ["reason"] = reason },
        };

    public static Error LocalConfigUnwritable(string path, string reason) =>
        new(ErrorCode.SteamLocalConfigUnwritable, $"Steam local config could not be read or written: {path} ({reason})", ErrorKind.External)
        {
            Details = new Dictionary<string, string> { ["path"] = path, ["reason"] = reason },
        };

    public static Error ClientRunning() =>
        new(ErrorCode.SteamClientRunning, "Steam is running; it would overwrite the change when it exits.", ErrorKind.Conflict);

    public static Error ShutdownTimedOut(TimeSpan timeout) =>
        new(ErrorCode.SteamShutdownTimedOut, $"Steam did not exit within {timeout.TotalSeconds:0} s.", ErrorKind.External)
        {
            Details = new Dictionary<string, string> { ["seconds"] = timeout.TotalSeconds.ToString("0", CultureInfo.InvariantCulture) },
        };

    public static Error ShutdownUnconfirmed(string reason) =>
        new(ErrorCode.SteamShutdownUnconfirmed, $"Steam's exit could not be checked: {reason}", ErrorKind.External)
        {
            Details = new Dictionary<string, string> { ["reason"] = reason },
        };

    public static Error StartFailed(string path, string reason) =>
        new(ErrorCode.SteamStartFailed, $"Steam could not be run: {path} ({reason})", ErrorKind.External)
        {
            Details = new Dictionary<string, string> { ["path"] = path, ["reason"] = reason },
        };
}
