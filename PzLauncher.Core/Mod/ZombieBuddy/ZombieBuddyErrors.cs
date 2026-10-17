using PzLauncher.Core.Common;

namespace PzLauncher.Core.Mod.ZombieBuddy;

internal static class ZombieBuddyErrors
{
    public static Error NotSubscribed(string itemId) =>
        new(ErrorCode.ZombieBuddyNotSubscribed, $"ZombieBuddy Workshop item {itemId} is not in any Steam library.", ErrorKind.NotFound)
        {
            Details = new Dictionary<string, string> { ["itemId"] = itemId },
        };

    public static Error SourceFileMissing(string path) =>
        new(ErrorCode.ZombieBuddySourceFileMissing, $"ZombieBuddy file is missing from the Workshop item: {path}", ErrorKind.NotFound)
        {
            Details = new Dictionary<string, string> { ["path"] = path },
        };

    public static Error CopyFailed(string path, string reason) =>
        new(ErrorCode.ZombieBuddyCopyFailed, $"ZombieBuddy file could not be copied to {path} ({reason})", ErrorKind.External)
        {
            Details = new Dictionary<string, string> { ["path"] = path, ["reason"] = reason },
        };

    public static Error DeleteFailed(string path, string reason) =>
        new(ErrorCode.ZombieBuddyDeleteFailed, $"ZombieBuddy file could not be deleted: {path} ({reason})", ErrorKind.External)
        {
            Details = new Dictionary<string, string> { ["path"] = path, ["reason"] = reason },
        };

    public static Error ConfigWriteFailed(string path, string reason) =>
        new(ErrorCode.ZombieBuddyConfigWriteFailed, $"Launch configuration could not be written: {path} ({reason})", ErrorKind.External)
        {
            Details = new Dictionary<string, string> { ["path"] = path, ["reason"] = reason },
        };

    public static Error NotInitialized(string path) =>
        new(ErrorCode.ZombieBuddyNotInitialized, $"Launch configuration has no ZombieBuddy agent argument: {path}", ErrorKind.Conflict)
        {
            Details = new Dictionary<string, string> { ["path"] = path },
        };

    // reason: one of the ZombieBuddyJavaMod reason constants; path: the mod.info or jar that fails it.
    public static Error ModNotPreloadable(string modId, string path, string reason) =>
        new(ErrorCode.ZombieBuddyModNotPreloadable, $"Java mod {modId} can't be preloaded: {path} ({reason})", ErrorKind.Validation)
        {
            Details = new Dictionary<string, string> { ["modId"] = modId, ["path"] = path, ["reason"] = reason },
        };

    public static Error PreloadConfigInvalid(string modId, string path, string reason) =>
        new(ErrorCode.ZombieBuddyPreloadConfigInvalid, $"ZombieBuddy config left untouched: {path} ({reason})", ErrorKind.Validation)
        {
            Details = new Dictionary<string, string> { ["modId"] = modId, ["path"] = path, ["reason"] = reason },
        };

    public static Error PreloadConfigIoFailed(string modId, string path, string reason) =>
        new(ErrorCode.ZombieBuddyPreloadConfigIoFailed, $"ZombieBuddy config could not be read or written: {path} ({reason})", ErrorKind.External)
        {
            Details = new Dictionary<string, string> { ["modId"] = modId, ["path"] = path, ["reason"] = reason },
        };
}
