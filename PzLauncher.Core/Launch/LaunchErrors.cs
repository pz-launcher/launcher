using System.Globalization;
using PzLauncher.Core.Common;
using PzLauncher.Core.Platform;

namespace PzLauncher.Core.Launch;

internal static class LaunchErrors
{
    public static Error PlatformUnsupported(PlatformKind os) =>
        new(ErrorCode.LaunchPlatformUnsupported, $"Launching the game is not supported on {os} yet.", ErrorKind.Validation);

    /// <param name="valueMb">The rejected value: a non-positive size, or a minimum above the maximum.</param>
    public static Error InvalidMemory(int valueMb) =>
        new(ErrorCode.LaunchInvalidMemory, $"Invalid memory setting: {valueMb} MB", ErrorKind.Validation)
        {
            Details = new Dictionary<string, string> { ["value"] = valueMb.ToString(CultureInfo.InvariantCulture) },
        };

    public static Error AlreadyRunning() =>
        new(ErrorCode.LaunchAlreadyRunning, "A game started by this launcher is still running.", ErrorKind.Conflict);

    public static Error NotAGame(string path) =>
        new(ErrorCode.LaunchNotAGame, $"Folder is not a Project Zomboid game install: {path}", ErrorKind.Validation)
        {
            Details = ErrorDetails.Path(path),
        };

    public static Error ConfigMissing(string path) =>
        new(ErrorCode.LaunchConfigMissing, $"Launch configuration does not exist: {path}", ErrorKind.NotFound)
        {
            Details = ErrorDetails.Path(path),
        };

    public static Error ConfigMalformed(string path, string reason) =>
        new(ErrorCode.LaunchConfigMalformed, $"Launch configuration could not be read: {path} ({reason})", ErrorKind.External)
        {
            Details = ErrorDetails.Path(path, reason),
        };

    public static Error ConfigWriteFailed(string path, string reason) =>
        new(ErrorCode.LaunchConfigWriteFailed, $"Launch configuration could not be written: {path} ({reason})", ErrorKind.External)
        {
            Details = ErrorDetails.Path(path, reason),
        };

    public static Error JavaMissing(string path) =>
        new(ErrorCode.LaunchJavaMissing, $"Java executable does not exist: {path}", ErrorKind.NotFound)
        {
            Details = ErrorDetails.Path(path),
        };

    public static Error StartFailed(string path, string reason) =>
        new(ErrorCode.LaunchStartFailed, $"Game process could not be started: {path} ({reason})", ErrorKind.External)
        {
            Details = ErrorDetails.Path(path, reason),
        };

    public static Error GcIncompatible(string javaHome) =>
        new(ErrorCode.LaunchGcIncompatible, $"The Graal JIT of {javaHome} doesn't support the Shenandoah GC.", ErrorKind.Validation)
        {
            Details = ErrorDetails.Path(javaHome),
        };

    public static Error GcIncompatibleViewpoint(string javaHome) =>
        new(ErrorCode.LaunchGcIncompatibleViewpoint, $"The Graal JIT of {javaHome} doesn't support the Shenandoah GC set by the Viewpoint mod.", ErrorKind.Validation)
        {
            Details = ErrorDetails.Path(javaHome),
        };

    public static Error GameCrashed(string path, int exitCode) =>
        new(ErrorCode.LaunchGameCrashed, $"The game's JVM crashed with exit code {exitCode}: {path}", ErrorKind.External)
        {
            Details = new Dictionary<string, string> { ["path"] = path, ["exitCode"] = exitCode.ToString(CultureInfo.InvariantCulture) },
        };

    /// <param name="output">The tail of the JVM's stderr; may be empty.</param>
    public static Error GameExitedEarly(int exitCode, string output) =>
        new(ErrorCode.LaunchGameExitedEarly, $"The game exited right after the start with exit code {exitCode}: {output}", ErrorKind.External)
        {
            Details = new Dictionary<string, string> { ["exitCode"] = exitCode.ToString(CultureInfo.InvariantCulture), ["output"] = output },
        };

    /// <param name="flags">The GC flags that select different collectors, space-separated.</param>
    public static Error GcConflict(string flags) =>
        new(ErrorCode.LaunchGcConflict, $"More than one garbage collector is selected: {flags}", ErrorKind.Validation)
        {
            Details = new Dictionary<string, string> { ["value"] = flags },
        };
}
