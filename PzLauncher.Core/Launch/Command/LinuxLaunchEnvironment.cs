namespace PzLauncher.Core.Launch.Command;

internal static class LinuxLaunchEnvironment
{
    public const string LibraryPathVariable = "LD_LIBRARY_PATH";
    public const string PreloadVariable = "LD_PRELOAD";
    public const string InputMethodVariable = "XMODIFIERS";

    // Mirrors the shipped B42 projectzomboid.sh (task research/linux-launch.md): natives such as fmod and steam_api
    // resolve through the loader path, not java.library.path, and the bare preload names resolve through it too, so
    // libjsig.so follows the chosen Java. The script clears XMODIFIERS so SCIM doesn't break keyboard input.
    public static Dictionary<string, string> Build(
        string gameFolder, string javaHome, string? inheritedLibraryPath, string? inheritedPreload) => new(StringComparer.Ordinal)
        {
            [LibraryPathVariable] = JoinNonEmpty(
                Path.Combine(gameFolder, PzLaunchFiles.NativesFolder),
                gameFolder,
                Path.Combine(javaHome, PzLaunchFiles.JavaLibFolder),
                inheritedLibraryPath),
            [PreloadVariable] = JoinNonEmpty([inheritedPreload, .. PzLaunchFiles.LinuxPreloadLibraries]),
            [InputMethodVariable] = "",
        };

    /// <summary>A colon-separated loader list without empty entries.</summary>
    public static string JoinNonEmpty(params string?[] parts) =>
        string.Join(':', parts.Where(p => !string.IsNullOrEmpty(p)));
}
