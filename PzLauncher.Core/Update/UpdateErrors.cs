using PzLauncher.Core.Common;

namespace PzLauncher.Core.Update;

internal static class UpdateErrors
{
    public static Error NetworkFailed(UpdateSource source, string reason) =>
        new(ErrorCode.UpdateNetworkFailed, $"{source} could not be reached ({reason})", ErrorKind.External)
        {
            Details = new Dictionary<string, string> { ["source"] = source.ToString(), ["reason"] = reason },
        };

    public static Error ReleaseInfoInvalid(UpdateSource source, string reason) =>
        new(ErrorCode.UpdateReleaseInfoInvalid, $"{source} sent unreadable release information ({reason})", ErrorKind.External)
        {
            Details = new Dictionary<string, string> { ["source"] = source.ToString(), ["reason"] = reason },
        };

    public static Error PackageInvalid(string package, string reason) =>
        new(ErrorCode.UpdatePackageInvalid, $"Update package {package} failed verification ({reason})", ErrorKind.External)
        {
            Details = new Dictionary<string, string> { ["package"] = package, ["reason"] = reason },
        };

    public static Error InstallFolderNotWritable(string path, string reason) =>
        new(ErrorCode.UpdateInstallFolderNotWritable, $"Launcher folder is not writable: {path} ({reason})", ErrorKind.Conflict)
        {
            Details = ErrorDetails.Path(path, reason),
        };

    public static Error InstallFailed(string path, string reason) =>
        new(ErrorCode.UpdateInstallFailed, $"Update could not be installed in {path}; previous files restored ({reason})", ErrorKind.External)
        {
            Details = ErrorDetails.Path(path, reason),
        };

    public static Error RestartFailed(string path, string reason) =>
        new(ErrorCode.UpdateRestartFailed, $"Updated launcher {path} could not be started ({reason})", ErrorKind.External)
        {
            Details = ErrorDetails.Path(path, reason),
        };

    public static Error DevelopmentBuild(SemVersion current) =>
        new(ErrorCode.UpdateDevelopmentBuild, $"Development build {current} does not update itself.", ErrorKind.Conflict)
        {
            Details = new Dictionary<string, string> { ["version"] = current.ToString() },
        };
}
