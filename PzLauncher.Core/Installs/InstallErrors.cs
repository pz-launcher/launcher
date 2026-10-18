using PzLauncher.Core.Common;

namespace PzLauncher.Core.Installs;

internal static class InstallErrors
{
    public static Error FilesMissing(string path, int appId) =>
        new(ErrorCode.InstallFilesMissing, $"Steam lists app {appId} but its folder is missing: {path}", ErrorKind.NotFound)
        {
            Details = new Dictionary<string, string>
            {
                ["path"] = path,
                ["appId"] = appId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            },
        };

    public static Error InvalidPath(string path) =>
        new(ErrorCode.InstallInvalidPath, $"Not a valid absolute path: '{path}'", ErrorKind.Validation)
        {
            Details = ErrorDetails.Path(path),
        };

    public static Error PathNotFound(string path) =>
        new(ErrorCode.InstallPathNotFound, $"Directory does not exist: {path}", ErrorKind.NotFound)
        {
            Details = ErrorDetails.Path(path),
        };

    public static Error PathUnreadable(string path, string reason) =>
        new(ErrorCode.InstallPathUnreadable, $"Directory could not be read: {path} ({reason})", ErrorKind.External)
        {
            Details = ErrorDetails.Path(path, reason),
        };

    public static Error NotAPzInstall(string path) =>
        new(ErrorCode.InstallNotAPzInstall, $"Directory is not a Project Zomboid game or server install: {path}", ErrorKind.Validation)
        {
            Details = ErrorDetails.Path(path),
        };
}
