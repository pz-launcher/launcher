using System.Globalization;
using PzLauncher.Core.Platform;
using PzLauncher.Core.Steam;
using PzLauncher.Core.Steam.Workshop;
using PzLauncher.Core.Tests.TestSupport;

namespace PzLauncher.Core.Tests.Steam.Workshop;

/// <summary>A signed-in Steam root, one library holding the game, and the game's default cache folder for its mods.</summary>
internal sealed class WorkshopTestLayout
{
    public WorkshopTestLayout(TempDirectory temp)
    {
        var steam = new SteamLayoutBuilder(temp.Path);
        Library = steam.CreateLibrary("Library");
        SteamRoot = steam.CreateSteamRoot(Library);
        Platform = FakePlatformEnvironment.WithRegistrySteam(SteamRoot);
        Platform.HomeDirectory = temp.CreateDirectory("home");
        SteamLayoutBuilder.WriteRaw(SteamClientFiles.LoginUsersPath(SteamRoot), Fixtures.Read("Steam", "loginusers.vdf"));
        Root = new GameInstallBuilder(Path.Combine(Library, "steamapps", "common"), PlatformKind.Windows)
            .CreateFlat("ProjectZomboid", GameInstallBuilder.WindowsGameJson, withJre: false);
        Mods = new ModBuilder(Path.Combine(Platform.HomeDirectory, "Zomboid"), Library);
    }

    public string SteamRoot { get; }

    public string Library { get; }

    public FakePlatformEnvironment Platform { get; }

    public string Root { get; }

    public ModBuilder Mods { get; }

    public string SubscriptionsPath
    {
        get
        {
            var account = ResultAssert.Ok(SteamLoginUsers.ParseCurrent(Fixtures.Read("Steam", "loginusers.vdf"), "p"));
            return WorkshopFiles.SubscriptionsPath(SteamRoot, account.AccountId);
        }
    }

    public void Subscribe(params (ulong Id, bool Disabled)[] items)
    {
        var entries = string.Concat(items.Select((item, i) =>
            $$"""
                "{{i}}" { "publishedfileid" "{{item.Id}}" "time_subscribed" "{{1000 + i}}" "disabled_locally" "{{(item.Disabled ? 1 : 0)}}" }

            """));
        SteamLayoutBuilder.WriteRaw(SubscriptionsPath, $"\"subscribedfiles\"\n{{\n\t\"appid\"\t\t\"108600\"\n{entries}}}\n");
    }

    public void Install(params (ulong Id, string Manifest, string Latest)[] items)
    {
        var installed = string.Concat(items.Select(item => $$"""    "{{item.Id}}" { "size" "42" "timeupdated" "500" "manifest" "{{item.Manifest}}" }""" + "\n"));
        var details = string.Concat(items.Select(item => $$"""    "{{item.Id}}" { "manifest" "{{item.Manifest}}" "latest_manifest" "{{item.Latest}}" }""" + "\n"));
        SteamLayoutBuilder.WriteRaw(
            WorkshopFiles.ManifestPath(Library),
            $"\"AppWorkshop\"\n{{\n\"WorkshopItemsInstalled\"\n{{\n{installed}}}\n\"WorkshopItemDetails\"\n{{\n{details}}}\n}}\n");
        foreach (var item in items)
        {
            Directory.CreateDirectory(ItemFolder(item.Id));
        }
    }

    public string ItemFolder(ulong id) => WorkshopFiles.ItemFolder(Library, id.ToString(CultureInfo.InvariantCulture));

    /// <summary>The game's collection file, one <c>name:id;id;</c> line each.</summary>
    public void WriteCollections(params string[] lines) =>
        SteamLayoutBuilder.WriteRaw(Path.Combine(Mods.Cache, "Lua", "pz_modlist_settings.cfg"), string.Concat(lines.Select(line => line + "\n")));

    /// <summary>The game's enabled mod list.</summary>
    public void WriteActive(params string[] ids) =>
        SteamLayoutBuilder.WriteRaw(
            Path.Combine(Mods.Cache, "mods", "default.txt"),
            $"VERSION = 1,\n\nmods\n{{\n{string.Concat(ids.Select(id => $"    mod = {id},\n"))}}}\n\nmaps\n{{\n}}\n");
}
