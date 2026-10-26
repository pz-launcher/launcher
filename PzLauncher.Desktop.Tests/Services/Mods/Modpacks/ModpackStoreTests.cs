using PzLauncher.Core.Mod;
using PzLauncher.Core.Mod.Collections;
using PzLauncher.Desktop.Services.Mods.Library;
using PzLauncher.Desktop.Services.Mods.LocalCollections;
using PzLauncher.Desktop.Services.Mods.Modpacks;
using PzLauncher.Desktop.Services.Mods;
using PzLauncher.Desktop.Tests.TestSupport;
using PzLauncher.Desktop.Services.Preferences.Records;

namespace PzLauncher.Desktop.Tests.Services.Mods.Modpacks;

public class ModpackStoreTests
{
    private const string GameRoot = @"E:\SteamLibrary\steamapps\common\ProjectZomboid";
    private const string OtherRoot = @"D:\Games\ProjectZomboid";

    private static readonly Guid ListId = new("6f1c2b0e-4d3a-4f5e-9a8b-1c2d3e4f5a6b");

    private static readonly Guid OtherListId = new("0a9b8c7d-6e5f-4a3b-8c2d-1e0f9a8b7c6d");

    private static readonly Modpack Server = new("Server", Preset("Survival", "Core", "Maps"), new ModpackCollection.Steam(9000), [1003]);

    private static readonly Modpack Night = new(
        "Night", Preset("Driving", "Cars"), new ModpackCollection.Local(ListId, "List", [new WorkshopEntry(1001, "Core")]), []);

    private readonly FakeModLibraryFile _file = new();

    private LocalCollectionStore _localCollections = TestWorkshop.LocalCollectionStore(
        null, new SavedLocalCollection(ListId, "List", [new SavedWorkshopEntry(1001, "Core")]));

    private static ModpackPreset Preset(string name, params string[] ids) => new(name, ids, IsLinked: true);

    private static ModCollection Game(string name, params string[] ids) => new(name, ids, IsActive: false, IsActiveInOrder: false);

    private ModpackStore Create(StringComparison pathComparison = StringComparison.OrdinalIgnoreCase, params Modpack[] saved) =>
        CreateWith(
            new Dictionary<string, IReadOnlyList<Modpack>>
            {
                [GameRoot] = saved.Length == 0 ? [Server, Night] : saved,
                [OtherRoot] = [Server with { Name = "Other install" }],
            },
            pathComparison: pathComparison);

    private ModpackStore CreateWith(
        Dictionary<string, IReadOnlyList<Modpack>> saved, bool localCollectionsComplete = true, StringComparison pathComparison = StringComparison.OrdinalIgnoreCase) =>
        TestWorkshop.ModpackStoreOf(_localCollections, _file, saved, localCollectionsComplete, pathComparison);

    private static Modpack Find(ModpackStore store, string name) => Assert.Single(store.For(GameRoot), modpack => modpack.Name == name);

    private static IReadOnlyList<Modpack> SavedFor(IReadOnlyDictionary<string, IReadOnlyList<SavedModpack>> saved, string root) =>
        [.. saved[root].Select(Modpack.FromSaved)];

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
        var saved = Assert.Single(_file.SavedModpacks);
        Assert.Equal([GameRoot, OtherRoot], saved.Keys);
        Assert.Equal(store.For(GameRoot), SavedFor(saved, GameRoot));
        Assert.Equal(store.For(OtherRoot), SavedFor(saved, OtherRoot));
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

        Assert.Empty(_file.SavedModpacks);
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
        Assert.Empty(_file.SavedModpacks);
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
        Assert.Single(_file.SavedModpacks);
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
    public void Delete_TheLastOne_SavesNoEntryForThatInstall()
    {
        var store = Create();

        store.Delete(OtherRoot, ["Other install"]);

        Assert.Empty(store.For(OtherRoot));
        Assert.Equal([GameRoot], Assert.Single(_file.SavedModpacks).Keys);
    }

    [Fact]
    public void SyncPresets_LinkedCopiesFollowTheirPresetAndAGonePresetMakesItIndependent()
    {
        var independent = new Modpack("Copy", new ModpackPreset("Gone", ["Old"], IsLinked: false), new ModpackCollection.Steam(9000), []);
        var store = Create(saved: [Server, Night, independent]);

        store.SyncPresets(GameRoot, [Game("Survival", "Core", "Maps", "Cars"), Game("Gone", "New")], [], _ => null);

        Assert.Equal(Preset("Survival", "Core", "Maps", "Cars"), Find(store, "Server").Preset);
        Assert.Equal(Night.Preset with { IsLinked = false }, Find(store, "Night").Preset);
        Assert.Equal(independent.Preset, Find(store, "Copy").Preset);
        Assert.Single(_file.SavedModpacks);
    }

    [Fact]
    public void SyncPresets_NothingChanged_SavesNothing()
    {
        var store = Create();

        store.SyncPresets(GameRoot, [Game("Survival", "Core", "Maps"), Game("Driving", "Cars")], [], _ => [1001]);

        Assert.Empty(_file.SavedModpacks);
    }

    [Fact]
    public void SyncPresets_RecomputesTheExtraItemsWhereTheCollectionIsKnownInTheSameSave()
    {
        var store = Create(saved: [Server with { ExtraItems = [] }, Night, Server with { Name = "Unread", Collection = new ModpackCollection.Steam(9001), ExtraItems = [] }]);
        IReadOnlyList<InstalledMod> installed = [TestMods.Mod("Core", workshopId: 1001), TestMods.Mod("Maps", workshopId: 1003), TestMods.Mod("Cars", workshopId: 1004)];

        store.SyncPresets(GameRoot, [Game("Survival", "Core", "Maps"), Game("Driving", "Cars")], installed, id => id == 9000 ? [1001] : null);

        Assert.Equal([1003UL], Find(store, "Server").ExtraItems);
        Assert.Equal([1004UL], Find(store, "Night").ExtraItems);
        Assert.Empty(Find(store, "Unread").ExtraItems);
        Assert.Single(_file.SavedModpacks);
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
        Assert.Equal(2, _file.SavedModpacks.Count);
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
    public void Load_LocalCollectionsIncomplete_KeepsLinksToMissingOnesThroughLaterChanges()
    {
        _localCollections = TestWorkshop.LocalCollectionStore(null, new SavedLocalCollection(OtherListId, "Other", []));
        var store = CreateWith(new Dictionary<string, IReadOnlyList<Modpack>> { [GameRoot] = [Server, Night] }, localCollectionsComplete: false);

        _localCollections.Rename(OtherListId, "Other B42");
        _localCollections.Create("New", []);

        Assert.Equal(Night.Collection, Find(store, "Night").Collection);
        Assert.Equal([new ModpackUser(GameRoot, "Night")], store.UsersOfLocalCollection(ListId));
        Assert.Empty(_file.SavedModpacks);
    }

    [Fact]
    public void Load_LocalCollectionsIncomplete_DeletingALoadedOneStillUnlinksItsUsers()
    {
        _localCollections = TestWorkshop.LocalCollectionStore(null, new SavedLocalCollection(OtherListId, "Other", []));
        var other = Night with { Name = "Other", Collection = new ModpackCollection.Local(OtherListId, "Other", []) };
        var store = CreateWith(new Dictionary<string, IReadOnlyList<Modpack>> { [GameRoot] = [Night, other] }, localCollectionsComplete: false);

        _localCollections.Delete(OtherListId);

        Assert.Equal(Night.Collection, Find(store, "Night").Collection);
        Assert.Equal(new ModpackCollection.Local(null, "Other", []), Find(store, "Other").Collection);
    }

    [Fact]
    public void LocalCollectionChange_UsedInSeveralInstalls_SavesAllOfThemOnce()
    {
        var store = Create();
        store.Save(OtherRoot, Night);
        _file.SavedModpacks.Clear();

        _localCollections.Rename(ListId, "List B42");

        var saved = Assert.Single(_file.SavedModpacks);
        Assert.Equal("List B42", Assert.IsType<SavedModpackCollection.Local>(saved[GameRoot][1].Collection).Name);
        Assert.Equal("List B42", Assert.IsType<SavedModpackCollection.Local>(saved[OtherRoot][1].Collection).Name);
        Assert.Equal(store.For(OtherRoot), SavedFor(saved, OtherRoot));
    }

    [Fact]
    public void UsersOfLocalCollection_ListsThemInEveryInstall()
    {
        var store = Create();
        store.Save(OtherRoot, Night);

        Assert.Equal([new ModpackUser(GameRoot, "Night"), new ModpackUser(OtherRoot, "Night")], store.UsersOfLocalCollection(ListId));
    }

    [Fact]
    public void DeleteUsersOfLocalCollection_DeletesThemInEveryInstallInOneSave()
    {
        var store = Create();
        store.Save(OtherRoot, Night);
        _file.SavedModpacks.Clear();

        store.DeleteUsersOfLocalCollection(ListId);

        Assert.Equal(["Server"], store.For(GameRoot).Select(modpack => modpack.Name));
        Assert.Equal(["Other install"], store.For(OtherRoot).Select(modpack => modpack.Name));
        Assert.Single(_file.SavedModpacks);
    }

    [Fact]
    public void UsersOfPreset_ListsOnlyLinkedOnesOfThatInstall()
    {
        var store = Create(saved: [Server, Server with { Name = "Copy", Preset = Server.Preset with { IsLinked = false } }]);

        Assert.Equal(["Server"], store.UsersOfPreset(GameRoot, "Survival"));
        Assert.Empty(store.UsersOfPreset(OtherRoot.ToUpperInvariant(), "Driving"));
    }

    [Fact]
    public void RefreshExtraItems_Changed_SavesThemForThatModpackOnce()
    {
        var store = Create();

        Assert.True(store.RefreshExtraItems(GameRoot, "Night", [TestMods.Mod("Cars", workshopId: 1003)], [1001]));

        Assert.Equal([1003UL], Find(store, "Night").ExtraItems);
        Assert.Equal([1003UL], Find(store, "Server").ExtraItems);
        Assert.Single(_file.SavedModpacks);
    }

    [Fact]
    public void RefreshExtraItems_Unchanged_SavesNothing()
    {
        var store = Create();

        Assert.False(store.RefreshExtraItems(GameRoot, "Server", [TestMods.Mod("Core", workshopId: 1001), TestMods.Mod("Maps", workshopId: 1003)], [1001]));
        Assert.False(store.RefreshExtraItems(GameRoot, "Missing", [], []));

        Assert.Empty(_file.SavedModpacks);
    }

    [Theory]
    [InlineData("Night", null, false)]
    [InlineData("Server", "Server", true)]
    [InlineData("New", null, true)]
    [InlineData(" ", null, false)]
    public void TrySave_TakenOrInvalidName_ChangesNothing(string name, string? original, bool saved)
    {
        var store = Create();

        Assert.Equal(saved, store.TrySave(GameRoot, Server with { Name = name, ExtraItems = [] }, original));

        Assert.Equal(saved, _file.SavedModpacks.Count == 1);
    }

    [Fact]
    public void EveryChange_RaisesChangedOnce()
    {
        var store = Create();
        var raised = 0;
        store.Changed += (_, _) => raised++;

        store.Save(GameRoot, Server with { Name = "New" });
        store.Rename(GameRoot, "New", "Renamed");
        store.RefreshExtraItems(GameRoot, "Renamed", [TestMods.Mod("Core", workshopId: 1001)], []);
        store.RenamePreset(GameRoot, "Survival", "Survival B42");
        store.Delete(GameRoot, ["Renamed"]);

        Assert.Equal(5, raised);
        Assert.Equal(5, _file.SavedModpacks.Count);
    }
}
