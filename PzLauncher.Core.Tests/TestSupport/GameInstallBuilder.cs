using PzLauncher.Core.Installs;
using PzLauncher.Core.Launch;
using PzLauncher.Core.Platform;

namespace PzLauncher.Core.Tests.TestSupport;

/// <summary>Fake game and server folders with a <c>ProjectZomboid64.json</c> and a bundled <c>jre64</c>.</summary>
public sealed class GameInstallBuilder(string baseDirectory, PlatformKind os)
{
    public static string WindowsGameJson => Fixtures.Read("Launch", "game-windows.json");

    public static string LinuxGameJson => Fixtures.Read("Launch", "game-linux.json");

    public static string ServerJson => Fixtures.Read("Launch", "server-windows.json");

    /// <summary>Windows layout: the json and <c>jre64</c> sit in the install root. Returns the root.</summary>
    public string CreateFlat(string name, string? json, bool withJre = true) =>
        Create(name, PzInstallMarkers.Game[0], nested: false, json, withJre);

    /// <summary>Linux Steam layout: <c>projectzomboid.sh</c> in the root, the game in <c>projectzomboid/</c>. Returns the root.</summary>
    public string CreateNested(string name, string? json, bool withJre = true) =>
        Create(name, "projectzomboid.sh", nested: true, json, withJre);

    public string CreateServer(string name) =>
        Create(name, PzInstallMarkers.DedicatedServer[0], nested: false, ServerJson, withJre: true);

    public static string GameFolder(string root, bool nested) =>
        nested ? Path.Combine(root, PzLaunchFiles.NestedGameFolder) : root;

    private string Create(string name, string marker, bool nested, string? json, bool withJre)
    {
        var root = Path.Combine(baseDirectory, name);
        var game = Directory.CreateDirectory(GameFolder(root, nested)).FullName;
        File.WriteAllText(Path.Combine(root, marker), "");
        if (json is not null)
        {
            File.WriteAllText(Path.Combine(game, PzLaunchFiles.ConfigFile), json);
        }

        if (withJre)
        {
            new JavaHomeBuilder(game, os).Create(PzLaunchFiles.BundledJreFolder, JavaHomeBuilder.Release("25.0.1", "Azul Systems, Inc."));
        }

        return root;
    }
}
