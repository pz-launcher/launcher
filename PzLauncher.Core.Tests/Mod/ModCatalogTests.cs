using Microsoft.Extensions.Logging.Abstractions;
using PzLauncher.Core.Mod;
using PzLauncher.Core.Tests.TestSupport;

namespace PzLauncher.Core.Tests.Mod;

public sealed class ModCatalogTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly ModBuilder _mods;

    public ModCatalogTests()
    {
        _mods = new ModBuilder(_temp.CreateDirectory("Zomboid"), _temp.CreateDirectory("Library"));
    }

    public void Dispose() => _temp.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private Task<IReadOnlyList<InstalledMod>> ReadAsync() =>
        ModCatalog.ReadAsync(_mods.Cache, [_mods.Library], NullLogger.Instance, Ct);

    [Fact]
    public async Task ReadAsync_EverySource_ListsModsInLookupOrderWithSourceAndWorkshopId()
    {
        _mods.Local("LocalMod");
        _mods.Steam("2000", "SteamMod");
        _mods.Staged("MyUpload", "StagedMod");

        var mods = await ReadAsync();

        Assert.Equal(
            [("StagedMod", InstalledModSource.StagedWorkshop, null), ("SteamMod", InstalledModSource.SteamWorkshop, (ulong?)2000), ("LocalMod", InstalledModSource.Local, null)],
            mods.Select(mod => (mod.Id, mod.Source, mod.WorkshopId)));
    }

    [Fact]
    public async Task ReadAsync_SameIdInSeveralRoots_FirstCopyWins()
    {
        _mods.Local("Shared", ModBuilder.ModInfo("Shared", name: "Local copy"));
        _mods.Steam("2000", "Shared", ModBuilder.ModInfo("Shared", name: "Steam copy"));

        var mod = Assert.Single(await ReadAsync());

        Assert.Equal(("Steam copy", InstalledModSource.SteamWorkshop), (mod.Name, mod.Source));
    }

    [Fact]
    public async Task ReadAsync_IdLists_AreSplitTrimmedAndStripTheBackslashPrefix()
    {
        _mods.Local("A", ModBuilder.ModInfo("A", require: @"\damnlib, B ,,", loadAfter: "C", loadBefore: @"\D", incompatible: "E,F"));

        var mod = Assert.Single(await ReadAsync());

        Assert.Equal(["damnlib", "B"], mod.Require);
        Assert.Equal(["C"], mod.LoadAfter);
        Assert.Equal(["D"], mod.LoadBefore);
        Assert.Equal(["E", "F"], mod.Incompatible);
    }

    [Fact]
    public async Task ReadAsync_FolderNameDiffersFromId_UsesTheModInfoIdAndNameFallsBackToIt()
    {
        ModBuilder.Create(Path.Combine(_mods.Cache, "mods", "folder"), "id=RealId\n");

        var mod = Assert.Single(await ReadAsync());

        Assert.Equal(("RealId", "RealId"), (mod.Id, mod.Name));
    }

    [Fact]
    public async Task ReadAsync_FolderWithoutModInfoOrId_IsSkipped()
    {
        _temp.CreateDirectory("Zomboid", "mods", "Empty", "42");
        ModBuilder.Create(Path.Combine(_mods.Cache, "mods", "NoId"), "name=No id\n");
        _mods.Local("Good");

        Assert.Equal(["Good"], (await ReadAsync()).Select(mod => mod.Id));
    }

    [Fact]
    public async Task ReadAsync_CommonModInfoOnly_IsListedUnderItsOwnId()
    {
        ModBuilder.Create(_mods.LocalModFolder("Ladders"), ModBuilder.ModInfo("Ladders42204"), "common");

        Assert.Equal(["Ladders42204"], (await ReadAsync()).Select(mod => mod.Id));
    }

    [Fact]
    public async Task ReadAsync_VersionFolderWithoutModInfo_ReadsCommonModInfo()
    {
        var folder = _mods.LocalModFolder("Viewpoint");
        _temp.CreateDirectory("Zomboid", "mods", "Viewpoint", "42", "media");
        ModBuilder.Create(folder, ModBuilder.ModInfo("Viewpoint", name: "Common copy"), "common");

        Assert.Equal("Common copy", Assert.Single(await ReadAsync()).Name);
    }

    [Fact]
    public async Task ReadAsync_VersionAndCommonModInfo_TheVersionFoldersWins()
    {
        var folder = _mods.LocalModFolder("Both");
        ModBuilder.Create(folder, ModBuilder.ModInfo("Both", name: "Version copy"));
        ModBuilder.Create(folder, ModBuilder.ModInfo("Both", name: "Common copy"), "common");

        Assert.Equal("Version copy", Assert.Single(await ReadAsync()).Name);
    }

    [Fact]
    public async Task ReadAsync_LowerVersionModInfoAndCommon_CommonWinsOverTheLowerVersion()
    {
        var folder = _mods.LocalModFolder("Lib");
        ModBuilder.Create(folder, ModBuilder.ModInfo("Lib", name: "Old copy"));
        _temp.CreateDirectory("Zomboid", "mods", "Lib", "42.15", "media");
        ModBuilder.Create(folder, ModBuilder.ModInfo("Lib", name: "Common copy"), "common");

        Assert.Equal("Common copy", Assert.Single(await ReadAsync()).Name);
    }

    [Fact]
    public async Task ReadAsync_LegacyRootModInfoOnly_IsNotListed()
    {
        var folder = _mods.LocalModFolder("Legacy");
        _temp.CreateDirectory("Zomboid", "mods", "Legacy", "media");
        await File.WriteAllTextAsync(Path.Combine(folder, "mod.info"), ModBuilder.ModInfo("Legacy"), Ct);

        Assert.Empty(await ReadAsync());
    }

    [Fact]
    public async Task ReadAsync_RootModInfoVersionWithoutModInfoAndCommon_ReadsCommonModInfo()
    {
        var folder = _mods.LocalModFolder("Progress");
        _temp.CreateDirectory("Zomboid", "mods", "Progress", "42");
        await File.WriteAllTextAsync(Path.Combine(folder, "mod.info"), ModBuilder.ModInfo("Progress", name: "Root copy"), Ct);
        ModBuilder.Create(folder, ModBuilder.ModInfo("Progress", name: "Common copy"), "common");

        Assert.Equal("Common copy", Assert.Single(await ReadAsync()).Name);
    }

    [Fact]
    public async Task ReadAsync_SameIdCommonOnlyInSeveralRoots_FirstCopyWins()
    {
        ModBuilder.Create(_mods.SteamModFolder("2000", "Ladders"), ModBuilder.ModInfo("Ladders42204", name: "Steam copy"), "common");
        ModBuilder.Create(_mods.LocalModFolder("Ladders"), ModBuilder.ModInfo("Ladders42204", name: "Local copy"), "common");

        var mod = Assert.Single(await ReadAsync());

        Assert.Equal(("Steam copy", (ulong?)2000), (mod.Name, mod.WorkshopId));
    }

    [Fact]
    public async Task ReadAsync_NoCache_ListsSteamModsOnly()
    {
        _mods.Local("LocalMod");
        _mods.Steam("2000", "SteamMod");

        var mods = await ModCatalog.ReadAsync(null, [_mods.Library], NullLogger.Instance, Ct);

        Assert.Equal(["SteamMod"], mods.Select(mod => mod.Id));
    }
}
