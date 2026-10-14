using PzLauncher.Core.Common;

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
}
