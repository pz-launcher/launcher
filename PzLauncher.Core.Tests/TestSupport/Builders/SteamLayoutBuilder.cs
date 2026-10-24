using PzLauncher.Core.Installs;
using PzLauncher.Core.Steam;

namespace PzLauncher.Core.Tests.TestSupport.Builders;

public sealed class SteamLayoutBuilder(string baseDirectory)
{
    public string CreateLibrary(string name)
    {
        var library = Path.Combine(baseDirectory, name);
        Directory.CreateDirectory(Path.Combine(library, "steamapps"));
        return library;
    }

    /// <summary>Creates the "Steam" root and lists it first in its libraryfolders.vdf.</summary>
    public string CreateSteamRoot(params string[] otherLibraries)
    {
        var root = CreateLibrary("Steam");
        WriteLibraryFolders(root, [root, .. otherLibraries]);
        return root;
    }

    /// <summary>Escapes backslashes like Steam does.</summary>
    public void WriteLibraryFolders(string steamRoot, params string[] libraryPaths)
    {
        var body = string.Concat(libraryPaths.Select((p, i) =>
            $"\t\"{i}\"\n\t{{\n\t\t\"path\"\t\t\"{Escape(p)}\"\n\t\t\"label\"\t\t\"\"\n\t\t\"apps\"\n\t\t{{\n\t\t}}\n\t}}\n"));
        WriteRaw(SteamLibraryReader.LibraryFoldersPath(steamRoot), $"\"libraryfolders\"\n{{\n{body}}}\n");
    }

    /// <summary>The created install folder contains the app's marker file.</summary>
    public string AddApp(string library, int appId, string installDir, string buildId = "1000", string? betaKey = null, bool createFolder = true)
    {
        var userConfig = betaKey is null
            ? "\t\"UserConfig\"\n\t{\n\t\t\"language\"\t\t\"english\"\n\t}\n"
            : $"\t\"UserConfig\"\n\t{{\n\t\t\"language\"\t\t\"english\"\n\t\t\"BetaKey\"\t\t\"{betaKey}\"\n\t}}\n";
        WriteRaw(
            SteamLibraryReader.ManifestPath(library, appId),
            $"\"AppState\"\n{{\n\t\"appid\"\t\t\"{appId}\"\n\t\"installdir\"\t\t\"{installDir}\"\n\t\"buildid\"\t\t\"{buildId}\"\n{userConfig}}}\n");

        var folder = Path.Combine(library, "steamapps", "common", installDir);
        if (createFolder)
        {
            Directory.CreateDirectory(folder);
            var marker = appId == PzSteamApps.DedicatedServer ? PzInstallMarkers.DedicatedServer[0] : PzInstallMarkers.Game[0];
            File.WriteAllText(Path.Combine(folder, marker), "");
        }

        return folder;
    }

    public static void WriteRaw(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static string Escape(string path) => path.Replace("\\", "\\\\", StringComparison.Ordinal);
}
