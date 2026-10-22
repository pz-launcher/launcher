using PzLauncher.Core.Mod.Collections;
using PzLauncher.Desktop.Services.Mods;
using PzLauncher.Desktop.Services.Preferences;
using PzLauncher.Desktop.Tests.TestSupport;

namespace PzLauncher.Desktop.Tests.Services.Mods;

public class ModpackStoreTests
{
    private const string GameRoot = @"E:\SteamLibrary\steamapps\common\ProjectZomboid";
    private const string OtherRoot = @"D:\Games\ProjectZomboid";

    private static readonly Guid ListId = new("6f1c2b0e-4d3a-4f5e-9a8b-1c2d3e4f5a6b");

    private static readonly Modpack Server = new("Server", Preset("Survival", "Core", "Maps"), new ModpackCollection.Steam(9000), [1003]);

    private static readonly Modpack Night = new(
        "Night", Preset("Driving", "Cars"), new ModpackCollection.Local(ListId, "List", [new WorkshopEntry(1001, "Core")]), []);

    private readonly FakePreferenceStore _store = new();

    private LocalCollections _localCollections = TestWorkshop.LocalCollectionStore(
        null, new SavedLocalCollection(ListId, "List", [new SavedWorkshopEntry(1001, "Core")]));

    private static ModpackPreset Preset(string name, params string[] ids) => new(name, ids, IsLinked: true);

    private static ModCollection Game(string name, params string[] ids) => new(name, ids, IsActive: false, IsActiveInOrder: false);

    private ModpackStore Create(StringComparison pathComparison = StringComparison.OrdinalIgnoreCase, params Modpack[] saved) =>
        new(
            _store,
            new UserPreferences(null, null, null)
            {
                Modpacks = new Dictionary<string, IReadOnlyList<Modpack>>
                {
                    [GameRoot] = saved.Length == 0 ? [Server, Night] : saved,
                    [OtherRoot] = [Server with { Name = "Other install" }],
                },
            },
            new FakePlatformEnvironment { PathComparison = pathComparison },
            _localCollections);

    private static Modpack Find(ModpackStore store, string name) => Assert.Single(store.For(GameRoot), modpack => modpack.Name == name);

    [Theory]
    [InlineData(StringComparison.OrdinalIgnoreCase, new[] { "Server", "Night" })]
    [InlineData(StringComparison.Ordinal, new string[0])]
    public void For_PathInOtherCase_FollowsThePlatformComparison(StringComparison comparison, string[] expected) =>
        Assert.Equal(expected, Create(comparison).For(GameRoot.ToUpperInvariant()).Select(modpack => modpack.Name));

    [Fact]
    public void For_EachInstallHasItsOwn()
    {
        var store = Create();

        Assert.Equal(["Other install"], store.For(OtherRoot).Select(modpack => modpack.Name));
        Assert.Empty(store.For(@"C:\Elsewhere"));
    }

    [Fact]
    public void Save_New_AppendsItTrimmedUnderTheSavedSpellingOfTheRoot()
    {
        var store = Create();

        store.Save(GameRoot.ToUpperInvariant(), Server with { Name = " New " });

        Assert.Equal(["Server", "Night", "New"], store.For(GameRoot).Select(modpack => modpack.Name));
        var (root, saved) = Assert.Single(_store.SavedModpacks);
        Assert.Equal(GameRoot, root);
        Assert.Equal(store.For(GameRoot), saved);
    }

    [Fact]
    public void Save_Edit_ReplacesItInItsPlace()
    {
        var store = Create();
        var edited = Server with { Name = "Server B42", Collection = new ModpackCollection.Steam(9001) };

        store.Save(GameRoot, edited, original: "Server");

        Assert.Equal([edited, Night], store.For(GameRoot));
    }

    [Fact]
    public void Save_Unchanged_SavesNothingAndRaisesNothing()
    {
        var store = Create();
        var raised = 0;
        store.Changed += (_, _) => raised++;

        store.Save(GameRoot, Server, original: "Server");

        Assert.Empty(_store.SavedModpacks);
        Assert.Equal(0, raised);
    }

    [Theory]
    [InlineData("Night")]
    [InlineData(" Night ")]
    [InlineData("")]
    [InlineData("Two\nlines")]
    public void Save_NameTakenOrInvalid_IsABug(string name)
    {
        var store = Create();

        Assert.Throws<ArgumentException>(() => store.Save(GameRoot, Server with { Name = name }));
        Assert.Throws<ArgumentException>(() => store.Rename(GameRoot, "Server", name));
        Assert.Empty(_store.SavedModpacks);
    }

    [Fact]
    public void IsNameTaken_IgnoresTheModpackBeingRenamedAndOtherInstalls()
    {
        var store = Create();

        Assert.True(store.IsNameTaken(GameRoot, " Night"));
        Assert.False(store.IsNameTaken(GameRoot, "Night", except: "Night"));
        Assert.False(store.IsNameTaken(GameRoot, "Other install"));
    }

    [Fact]
    public void Rename_KeepsItsPlaceAndContents()
    {
        var store = Create();

        store.Rename(GameRoot, "Server", "Server B42");

        Assert.Equal([Server with { Name = "Server B42" }, Night], store.For(GameRoot));
        Assert.Single(_store.SavedModpacks);
    }

    [Fact]
    public void Delete_RemovesOnlyThoseOfThatInstall()
    {
        var store = Create();

        store.Delete(GameRoot, ["Server", "Missing"]);

        Assert.Equal([Night], store.For(GameRoot));
        Assert.Single(store.For(OtherRoot));
    }

    [Fact]
    public void Delete_TheLastOne_SavesAnEmptyListForThatInstall()
    {
        var store = Create();

        store.Delete(OtherRoot, ["Other install"]);

        Assert.Empty(store.For(OtherRoot));
        var (root, saved) = Assert.Single(_store.SavedModpacks);
        Assert.Equal(OtherRoot, root);
        Assert.Empty(saved);
    }

    [Fact]
    public void SyncPresets_LinkedCopiesFollowTheirPresetAndAGonePresetMakesItIndependent()
    {
        var independent = new Modpack("Copy", new ModpackPreset("Gone", ["Old"], IsLinked: false), new ModpackCollection.Steam(9000), []);
        var store = Create(saved: [Server, Night, independent]);

        store.SyncPresets(GameRoot, [Game("Survival", "Core", "Maps", "Cars"), Game("Gone", "New")]);

        Assert.Equal(Preset("Survival", "Core", "Maps", "Cars"), Find(store, "Server").Preset);
        Assert.Equal(Night.Preset with { IsLinked = false }, Find(store, "Night").Preset);
        Assert.Equal(independent.Preset, Find(store, "Copy").Preset);
        Assert.Single(_store.SavedModpacks);
    }

    [Fact]
    public void SyncPresets_NothingChanged_SavesNothing()
    {
        var store = Create();

        store.SyncPresets(GameRoot, [Game("Survival", "Core", "Maps"), Game("Driving", "Cars")]);

        Assert.Empty(_store.SavedModpacks);
    }

    [Fact]
    public void RenamePreset_LinksFollowIt()
    {
        var store = Create();

        store.RenamePreset(GameRoot, "Survival", "Survival B42");

        Assert.Equal(Preset("Survival B42", "Core", "Maps"), Find(store, "Server").Preset);
        Assert.Equal(["Server"], store.UsersOfPreset(GameRoot, "Survival B42"));
        Assert.Empty(store.UsersOfPreset(GameRoot, "Survival"));
    }

    [Fact]
    public void UnlinkPreset_KeepsTheCopy()
    {
        var store = Create();

        store.UnlinkPreset(GameRoot, "Survival");

        Assert.Equal(Server.Preset with { IsLinked = false }, Find(store, "Server").Preset);
        Assert.Empty(store.UsersOfPreset(GameRoot, "Survival"));
    }

    [Fact]
    public void LinkPreset_FollowsThatPresetFromNowOn()
    {
        var store = Create(saved: [Server with { Preset = new ModpackPreset("Old", ["Core"], IsLinked: false) }]);

        store.LinkPreset(GameRoot, "Server", "Old (2)");

        Assert.Equal(new ModpackPreset("Old (2)", ["Core"], IsLinked: true), Find(store, "Server").Preset);
    }

    [Fact]
    public void LocalCollectionChanges_LinkedCopiesFollowThem()
    {
        var store = Create();

        _localCollections.Rename(ListId, "List B42");
        _localCollections.Add(ListId, [new WorkshopEntry(1002, "Maps")]);

        Assert.Equal(
            new ModpackCollection.Local(ListId, "List B42", [new WorkshopEntry(1001, "Core"), new WorkshopEntry(1002, "Maps")]),
            Find(store, "Night").Collection);
        Assert.Equal(2, _store.SavedModpacks.Count);
    }

    [Fact]
    public void LocalCollectionDeleted_ItsUsersKeepTheirCopy()
    {
        var store = Create();

        _localCollections.Delete(ListId);

        Assert.Equal(new ModpackCollection.Local(null, "List", [new WorkshopEntry(1001, "Core")]), Find(store, "Night").Collection);
        Assert.Empty(store.UsersOfLocalCollection(ListId));
    }

    [Fact]
    public void Load_LinkToALocalCollectionNoLongerStored_IsIndependent()
    {
        _localCollections = TestWorkshop.LocalCollectionStore();

        var store = Create();

        Assert.Equal(new ModpackCollection.Local(null, "List", [new WorkshopEntry(1001, "Core")]), Find(store, "Night").Collection);
    }

    [Fact]
    public void UsersOfLocalCollection_ListsThemInEveryInstall()
    {
        var store = Create();
        store.Save(OtherRoot, Night);

        Assert.Equal([new ModpackUser(GameRoot, "Night"), new ModpackUser(OtherRoot, "Night")], store.UsersOfLocalCollection(ListId));
    }

    [Fact]
    public void UsersOfPreset_ListsOnlyLinkedOnesOfThatInstall()
    {
        var store = Create(saved: [Server, Server with { Name = "Copy", Preset = Server.Preset with { IsLinked = false } }]);

        Assert.Equal(["Server"], store.UsersOfPreset(GameRoot, "Survival"));
        Assert.Empty(store.UsersOfPreset(OtherRoot.ToUpperInvariant(), "Driving"));
    }

    [Fact]
    public void SetExtraItems_SavesThemForThatModpack()
    {
        var store = Create();

        store.SetExtraItems(GameRoot, "Night", [1005, 1006]);

        Assert.Equal([1005UL, 1006], Find(store, "Night").ExtraItems);
        Assert.Equal([1003UL], Find(store, "Server").ExtraItems);
    }

    [Fact]
    public void EveryChange_RaisesChangedOnce()
    {
        var store = Create();
        var raised = 0;
        store.Changed += (_, _) => raised++;

        store.Save(GameRoot, Server with { Name = "New" });
        store.Rename(GameRoot, "New", "Renamed");
        store.SetExtraItems(GameRoot, "Renamed", [1]);
        store.RenamePreset(GameRoot, "Survival", "Survival B42");
        store.Delete(GameRoot, ["Renamed"]);

        Assert.Equal(5, raised);
        Assert.Equal(5, _store.SavedModpacks.Count);
    }
}
