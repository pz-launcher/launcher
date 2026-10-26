using PzLauncher.Core.Common;
using PzLauncher.Core.Mod.Collections;
using PzLauncher.Core.Mod.Modpacks;
using PzLauncher.Desktop.Services.Mods;
using PzLauncher.Desktop.Services.Mods.Library;
using PzLauncher.Desktop.Services.Mods.LocalCollections;
using PzLauncher.Desktop.Services.Mods.Modpacks;
using PzLauncher.Desktop.Services.Preferences.Records;
using PzLauncher.Desktop.Tests.TestSupport;

namespace PzLauncher.Desktop.Tests.Services.Mods.Modpacks;

public class ModpackImportTests
{
    private const string Root = @"E:\SteamLibrary\steamapps\common\ProjectZomboid";

    private static readonly Guid ListId = new("6f1c2b0e-4d3a-4f5e-9a8b-1c2d3e4f5a6b");

    private static readonly Modpack Server = new("Server", new ModpackPreset("Survival", ["Core"], IsLinked: true), new ModpackCollection.Steam(9000), []);

    private static readonly SharedModpack SteamShared = new(
        "Shared", new SharedModpackCollection.Steam(9001), [1003], new SharedModCollection("Shared", ["Core", "Cars"]));

    private static readonly SharedModpack LocalShared = new(
        "Shared", new SharedModpackCollection.Local("Friends", [1004, 1001]), [], new SharedModCollection("Shared", ["Core"]));

    private readonly FakeModCollectionService _service = new FakeModCollectionService().WithCollection("Survival", "Core");
    private readonly FakeModLibraryFile _file = new();
    private readonly WorkshopCollectionHistory _history = TestWorkshop.History();
    private readonly LocalCollectionStore _localCollections;
    private readonly ModpackStore _modpacks;
    private readonly ModpackImport _import;

    public ModpackImportTests()
    {
        _localCollections = TestWorkshop.LocalCollectionStore(_file, new SavedLocalCollection(ListId, "List", [new SavedWorkshopEntry(1002, "Map Pack")]));
        _modpacks = TestWorkshop.ModpackStoreOf(_localCollections, _file, new Dictionary<string, IReadOnlyList<Modpack>> { [Root] = [Server] });
        _import = new ModpackImport(new PresetWriter(_service, new FakeModCollectionDefaultsService(), _modpacks), _localCollections, _modpacks, _history);
    }

    private static ModpackImportNames Names(string modpack, string preset = "Shared", ModpackImportLocalName? local = null, bool replaces = false) =>
        new(modpack, replaces, preset, local);

    private Task<ModpackImportResult> ImportAsync(SharedModpack shared, ModpackImportNames names, string? steamTitle = null) =>
        _import.ImportAsync(Root, "-debug", shared, names, steamTitle, TestContext.Current.CancellationToken);

    private Modpack Saved(string name) => Assert.Single(_modpacks.For(Root), modpack => modpack.Name == name);

    [Fact]
    public async Task SteamCollection_WritesThePresetRemembersTheCollectionAndSavesTheModpackLinked()
    {
        var result = await ImportAsync(SteamShared, Names("Shared"), "Survival Pack");

        Assert.Equal(new ModpackImportResult.Imported("Shared"), result);
        Assert.Equal(["Core", "Cars"], _service.IdsOf("Shared"));
        Assert.Equal("-debug", Assert.Single(_service.Saves).ExtraGameArgs);
        Assert.Equal(new WorkshopEntry(9001, "Survival Pack"), Assert.Single(_history.Items));
        Assert.Equal(
            new Modpack("Shared", new ModpackPreset("Shared", ["Core", "Cars"], IsLinked: true), new ModpackCollection.Steam(9001), [1003]), Saved("Shared"));
    }

    [Fact]
    public async Task LocalCollection_New_CreatesItAndLinksTheModpackToIt()
    {
        var result = await ImportAsync(LocalShared, Names("Shared", local: new ModpackImportLocalName("Friends", null)));

        Assert.Equal(new ModpackImportResult.Imported("Shared"), result);
        var list = Assert.Single(_localCollections.All, collection => collection.Name == "Friends");
        Assert.Equal([new WorkshopEntry(1004, null), new WorkshopEntry(1001, null)], list.Items);
        Assert.Equal(new ModpackCollection.Local(list.Id, "Friends", list.Items), Saved("Shared").Collection);
        Assert.Empty(_history.Items);
    }

    [Fact]
    public async Task EveryNameTaken_Overwrite_ReplacesEachInPlace()
    {
        var result = await ImportAsync(LocalShared, Names("Server", "Survival", new ModpackImportLocalName("List", ListId), replaces: true));

        Assert.Equal(new ModpackImportResult.Imported("Server"), result);
        Assert.Equal(["Core"], _service.IdsOf("Survival"));
        Assert.Equal([1004UL, 1001], _localCollections.Find(ListId)?.Items.Select(item => item.Id));
        Assert.Single(_localCollections.All);
        var imported = Assert.Single(_modpacks.For(Root));
        Assert.Equal(ListId, Assert.IsType<ModpackCollection.Local>(imported.Collection).Linked);
    }

    [Fact]
    public async Task ReplacedLocalCollectionDeletedMeanwhile_IsMadeAgain()
    {
        _localCollections.Delete(ListId);

        var result = await ImportAsync(LocalShared, Names("Shared", local: new ModpackImportLocalName("List", ListId)));

        Assert.Equal(new ModpackImportResult.Imported("Shared"), result);
        var list = Assert.Single(_localCollections.All);
        Assert.Equal(("List", list.Id), (list.Name, Assert.IsType<ModpackCollection.Local>(Saved("Shared").Collection).Linked));
    }

    [Fact]
    public async Task ModpackNameTaken_WritesNothing()
    {
        var result = await ImportAsync(SteamShared, Names("Server"));

        Assert.Equal(new ModpackImportResult.NameTaken(ModpackImportNameKind.Modpack, "Server"), result);
        Assert.Empty(_service.Saves);
        Assert.Empty(_history.Items);
        Assert.Equal([Server], _modpacks.For(Root));
    }

    [Fact]
    public async Task LocalCollectionNameTaken_WritesNothing()
    {
        var result = await ImportAsync(LocalShared, Names("Shared", local: new ModpackImportLocalName("List", null)));

        Assert.Equal(new ModpackImportResult.NameTaken(ModpackImportNameKind.LocalCollection, "List"), result);
        Assert.Empty(_service.Saves);
        Assert.Single(_localCollections.All);
        Assert.Equal([Server], _modpacks.For(Root));
    }

    [Fact]
    public async Task ModpackNameTakenDuringThePresetWrite_KeepsThePresetAndTheOtherModpack()
    {
        _service.SaveGate = new TaskCompletionSource();
        var import = ImportAsync(SteamShared, Names("Night"));
        var other = Server with { Name = "Night" };

        _modpacks.Save(Root, other);
        _service.SaveGate.SetResult();

        Assert.Equal(new ModpackImportResult.NameTaken(ModpackImportNameKind.Modpack, "Night"), await import);
        Assert.Equal(["Core", "Cars"], _service.IdsOf("Shared"));
        Assert.Equal(other, Saved("Night"));
    }

    [Fact]
    public async Task LocalCollectionNameTakenDuringThePresetWrite_SavesNoModpack()
    {
        _service.SaveGate = new TaskCompletionSource();
        var import = ImportAsync(LocalShared, Names("Shared", local: new ModpackImportLocalName("Friends", null)));

        _localCollections.Create("Friends", []);
        _service.SaveGate.SetResult();

        Assert.Equal(new ModpackImportResult.NameTaken(ModpackImportNameKind.LocalCollection, "Friends"), await import);
        Assert.Empty(Assert.Single(_localCollections.All, collection => collection.Name == "Friends").Items);
        Assert.Equal([Server], _modpacks.For(Root));
    }

    [Fact]
    public async Task PresetWriteFails_ReturnsTheErrorAndWritesNothingElse()
    {
        _service.SaveError = _ => TestErrors.Create(ErrorCode.ModCollectionFileWriteFailed, @"C:\Users\Test\Zomboid\Lua\saved_modlists.txt");

        var result = await ImportAsync(SteamShared, Names("Shared"));

        Assert.Equal(ErrorCode.ModCollectionFileWriteFailed, Assert.IsType<ModpackImportResult.Failed>(result).Error.Code);
        Assert.Empty(_history.Items);
        Assert.Equal([Server], _modpacks.For(Root));
    }
}
