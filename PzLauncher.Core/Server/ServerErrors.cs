using System.Globalization;
using PzLauncher.Core.Common;

namespace PzLauncher.Core.Server;

internal static class ServerErrors
{
    // Unreadable, not UTF-8 or unwritable: for the host each means the ini can't be used.
    public static GameTextFileErrors TextFile { get; } = new(FileAccessFailed, FileAccessFailed, FileAccessFailed);

    public static Error NameInvalid(string name) =>
        new(ErrorCode.ServerNameInvalid, $"Not a valid server name: '{name}'", ErrorKind.Validation)
        {
            Details = Name(name),
        };

    public static Error AlreadyExists(string name) =>
        new(ErrorCode.ServerAlreadyExists, $"A server with this name already exists: {name}", ErrorKind.Conflict)
        {
            Details = Name(name),
        };

    public static Error NotFound(string name) =>
        new(ErrorCode.ServerNotFound, $"No server with this name exists: {name}", ErrorKind.NotFound)
        {
            Details = Name(name),
        };

    public static Error NotAServerInstall(string path) =>
        new(ErrorCode.ServerNotAServerInstall, $"Folder is not a Project Zomboid dedicated server install: {path}", ErrorKind.Validation)
        {
            Details = ErrorDetails.Path(path),
        };

    /// <param name="name">The server that runs.</param>
    public static Error AlreadyRunning(string name) =>
        new(ErrorCode.ServerAlreadyRunning, $"Server {name} is already running.", ErrorKind.Conflict)
        {
            Details = Name(name),
        };

    public static Error NotRunning() =>
        new(ErrorCode.ServerNotRunning, "No server is running.", ErrorKind.Conflict);

    public static Error NotReady() =>
        new(ErrorCode.ServerNotReady, "The server is still starting: RCON hasn't answered yet.", ErrorKind.Conflict);

    public static Error AdminPasswordRequired(string name) =>
        new(ErrorCode.ServerAdminPasswordRequired, $"Server {name} has no admin account yet: an admin password is required.", ErrorKind.Validation)
        {
            Details = Name(name),
        };

    public static Error StartFailed(string path, string reason) =>
        new(ErrorCode.ServerStartFailed, $"Server process could not be started: {path} ({reason})", ErrorKind.External)
        {
            Details = ErrorDetails.Path(path, reason),
        };

    /// <param name="output">The console's last lines of this start; may be empty.</param>
    public static Error ExitedEarly(string name, string output) =>
        new(ErrorCode.ServerExitedEarly, $"Server {name} exited before it was ready: {output}", ErrorKind.External)
        {
            Details = new Dictionary<string, string> { ["name"] = name, ["output"] = output },
        };

    /// <param name="output">The console's last lines; may be empty.</param>
    public static Error ExitedUnexpectedly(string name, string output) =>
        new(ErrorCode.ServerExitedUnexpectedly, $"Server {name} exited without being stopped: {output}", ErrorKind.External)
        {
            Details = new Dictionary<string, string> { ["name"] = name, ["output"] = output },
        };

    public static Error RconFailed(string reason) =>
        new(ErrorCode.ServerRconFailed, $"RCON request failed: {reason}", ErrorKind.External)
        {
            Details = ErrorDetails.Reason(reason),
        };

    public static Error CommandInvalid() =>
        new(ErrorCode.ServerCommandInvalid, "A command must be one non-empty line of at most 4000 UTF-8 bytes.", ErrorKind.Validation);

    public static Error StopTimeout(TimeSpan timeout) =>
        new(ErrorCode.ServerStopTimeout, $"The server didn't exit within {timeout.TotalSeconds:0} seconds.", ErrorKind.External)
        {
            Details = new Dictionary<string, string> { ["seconds"] = timeout.TotalSeconds.ToString("0", CultureInfo.InvariantCulture) },
        };

    public static Error FileAccessFailed(string path, string reason) =>
        new(ErrorCode.ServerFileAccessFailed, $"Server file could not be read or written: {path} ({reason})", ErrorKind.External)
        {
            Details = ErrorDetails.Path(path, reason),
        };

    private static Dictionary<string, string> Name(string name) => new() { ["name"] = name };
}
