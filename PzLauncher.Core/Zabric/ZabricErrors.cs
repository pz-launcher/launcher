using PzLauncher.Core.Common;

namespace PzLauncher.Core.Zabric;

internal static class ZabricErrors
{
    // reason: one of the ZabricModInfo reason constants; path: the mod.info or jar that fails it.
    public static Error NotPreloadable(string path, string reason) =>
        new(ErrorCode.ZabricNotPreloadable, $"Zabric can't be preloaded: {path} ({reason})", ErrorKind.Validation)
        {
            Details = new Dictionary<string, string> { ["path"] = path, ["reason"] = reason },
        };

    public static Error PreloadConfigInvalid(string path, string reason) =>
        new(ErrorCode.ZabricPreloadConfigInvalid, $"ZombieBuddy config left untouched: {path} ({reason})", ErrorKind.Validation)
        {
            Details = new Dictionary<string, string> { ["path"] = path, ["reason"] = reason },
        };

    public static Error PreloadConfigIoFailed(string path, string reason) =>
        new(ErrorCode.ZabricPreloadConfigIoFailed, $"ZombieBuddy config could not be read or written: {path} ({reason})", ErrorKind.External)
        {
            Details = new Dictionary<string, string> { ["path"] = path, ["reason"] = reason },
        };
}
