using PzLauncher.Core.Java;
using PzLauncher.Core.Platform;

namespace PzLauncher.Core.Launch;

internal static class PzLaunchFiles
{
    public const string ConfigFile = "ProjectZomboid64.json";

    public const string BundledJreFolder = "jre64";

    // Linux Steam depot: projectzomboid.sh sits in the app folder and cd's into this subfolder.
    public const string NestedGameFolder = "projectzomboid";

    public const string NativesFolder = "natives";

    public const string JavaLibFolder = "lib";

    /// <summary>Bare names, as in <c>projectzomboid.sh</c>: the loader finds them through <c>LD_LIBRARY_PATH</c>.</summary>
    public static IReadOnlyList<string> LinuxPreloadLibraries { get; } = ["libjsig.so", "libPZXInitThreads64.so"];

    public static string JavaExecutable(string javaHome, PlatformKind os) =>
        Path.Combine(javaHome, JavaLocations.BinFolder, JavaLocations.ExecutableName(os));
}
