using System.Globalization;
using PzLauncher.Core.Common;

namespace PzLauncher.Core.Steam.Workshop;

internal static class WorkshopErrors
{
    public static Error SubscriptionsUnreadable(string path, string reason) =>
        new(ErrorCode.WorkshopSubscriptionsUnreadable, $"Steam Workshop subscription list could not be read: {path} ({reason})", ErrorKind.External)
        {
            Details = ErrorDetails.Path(path, reason),
        };

    public static Error ManifestUnreadable(string path, string reason) =>
        new(ErrorCode.WorkshopManifestUnreadable, $"Steam Workshop install list could not be read: {path} ({reason})", ErrorKind.External)
        {
            Details = ErrorDetails.Path(path, reason),
        };

    public static Error NetworkFailed(string reason) =>
        new(ErrorCode.WorkshopNetworkFailed, $"Steam Web API could not be reached ({reason})", ErrorKind.External)
        {
            Details = new Dictionary<string, string> { ["reason"] = reason },
        };

    public static Error ResponseInvalid(string reason) =>
        new(ErrorCode.WorkshopResponseInvalid, $"Steam Web API sent an unreadable response ({reason})", ErrorKind.External)
        {
            Details = new Dictionary<string, string> { ["reason"] = reason },
        };

    public static Error CollectionNotFound(ulong id) =>
        new(ErrorCode.WorkshopCollectionNotFound, $"Workshop item {id} is not a visible collection.", ErrorKind.NotFound)
        {
            Details = new Dictionary<string, string> { ["id"] = id.ToString(CultureInfo.InvariantCulture) },
        };

    public static Error SteamApiMissing(string installRoot) =>
        new(ErrorCode.WorkshopSteamApiMissing, $"The game install has no steam_api library: {installRoot}", ErrorKind.NotFound)
        {
            Details = ErrorDetails.Path(installRoot),
        };

    public static Error SteamApiUnusable(string path, string reason) =>
        new(ErrorCode.WorkshopSteamApiUnusable, $"The game's steam_api library can't be used: {path} ({reason})", ErrorKind.External)
        {
            Details = ErrorDetails.Path(path, reason),
        };

    public static Error SteamNotRunning() =>
        new(ErrorCode.WorkshopSteamNotRunning, "Steam isn't running or isn't signed in.", ErrorKind.Conflict);

    public static Error SteamInitFailed(string reason) =>
        new(ErrorCode.WorkshopSteamInitFailed, $"Steam refused to start the Workshop helper ({reason})", ErrorKind.External)
        {
            Details = new Dictionary<string, string> { ["reason"] = reason },
        };

    public static Error HelperStartFailed(string path, string reason) =>
        new(ErrorCode.WorkshopHelperStartFailed, $"The Steam helper could not be started: {path} ({reason})", ErrorKind.External)
        {
            Details = ErrorDetails.Path(path, reason),
        };

    public static Error HelperFailed(string reason) =>
        new(ErrorCode.WorkshopHelperFailed, $"The Steam helper stopped without a result ({reason})", ErrorKind.Unexpected)
        {
            Details = new Dictionary<string, string> { ["reason"] = reason },
        };

    public static Error HelperTimedOut(TimeSpan timeout) =>
        new(ErrorCode.WorkshopHelperTimedOut, $"The Steam helper reported nothing for {timeout.TotalSeconds:0} s.", ErrorKind.External)
        {
            Details = new Dictionary<string, string> { ["seconds"] = timeout.TotalSeconds.ToString("0", CultureInfo.InvariantCulture) },
        };
}
