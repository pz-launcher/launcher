namespace PzLauncher.Core.Installs;

// Verified against real Windows installs (task research/install-markers.md); macOS/Linux names are not.
internal static class PzInstallMarkers
{
    // Checked before Game: a server folder can contain game-like files.
    // ProjectZomboidServer.bat ships in the *game* folder, so it's not a server marker.
    public static IReadOnlyList<string> DedicatedServer { get; } =
    [
        "StartServer64.bat",
        "start-server.sh",
    ];

    public static IReadOnlyList<string> Game { get; } =
    [
        "ProjectZomboid64.exe",
        "ProjectZomboid64",
        "projectzomboid.sh",
        "Project Zomboid.app",
    ];
}
