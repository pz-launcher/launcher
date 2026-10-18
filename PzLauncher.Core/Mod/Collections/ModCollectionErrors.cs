using PzLauncher.Core.Common;

namespace PzLauncher.Core.Mod.Collections;

internal static class ModCollectionErrors
{
    public static Error CacheFolderUnresolved(string installRoot) =>
        new(ErrorCode.ModCollectionCacheFolderUnresolved, $"The game's cache folder could not be resolved for {installRoot} (-cachedir= or home folder).", ErrorKind.Validation)
        {
            Details = ErrorDetails.Path(installRoot),
        };

    public static Error NameInvalid(string name) =>
        new(ErrorCode.ModCollectionNameInvalid, $"Not a valid collection name: '{name}'", ErrorKind.Validation)
        {
            Details = new Dictionary<string, string> { ["name"] = name },
        };

    public static Error ModIdInvalid(string modId) =>
        new(ErrorCode.ModCollectionModIdInvalid, $"Not a valid mod id for a collection: '{modId}'", ErrorKind.Validation)
        {
            Details = new Dictionary<string, string> { ["modId"] = modId },
        };

    public static Error NotFound(string name) =>
        new(ErrorCode.ModCollectionNotFound, $"No collection named '{name}'.", ErrorKind.NotFound)
        {
            Details = new Dictionary<string, string> { ["name"] = name },
        };

    public static Error FileUnreadable(string path, string reason) =>
        new(ErrorCode.ModCollectionFileUnreadable, $"Mod list file could not be read: {path} ({reason})", ErrorKind.External)
        {
            Details = ErrorDetails.Path(path, reason),
        };

    public static Error FileMalformed(string path, string reason) =>
        new(ErrorCode.ModCollectionFileMalformed, $"Mod list file left untouched: {path} ({reason})", ErrorKind.External)
        {
            Details = ErrorDetails.Path(path, reason),
        };

    public static Error FileWriteFailed(string path, string reason) =>
        new(ErrorCode.ModCollectionFileWriteFailed, $"Mod list file could not be written: {path} ({reason})", ErrorKind.External)
        {
            Details = ErrorDetails.Path(path, reason),
        };
}
