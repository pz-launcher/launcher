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
            [("StagedMod", InstalledModSource.StagedWorkshop, null), ("SteamMod", InstalledModSource.SteamWorkshop, "2000"), ("LocalMod", InstalledModSource.Local, null)],
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
    public async Task ReadAsync_NoCache_ListsSteamModsOnly()
    {
        _mods.Local("LocalMod");
        _mods.Steam("2000", "SteamMod");

        var mods = await ModCatalog.ReadAsync(null, [_mods.Library], NullLogger.Instance, Ct);

        Assert.Equal(["SteamMod"], mods.Select(mod => mod.Id));
    }
}
