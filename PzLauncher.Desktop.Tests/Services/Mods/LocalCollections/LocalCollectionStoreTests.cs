using PzLauncher.Desktop.Services.Mods.Library;
using PzLauncher.Desktop.Services.Mods.LocalCollections;
using PzLauncher.Desktop.Services.Mods;
using PzLauncher.Desktop.Tests.TestSupport;
using PzLauncher.Desktop.Services.Preferences.Records;

namespace PzLauncher.Desktop.Tests.Services.Mods.LocalCollections;

public class LocalCollectionStoreTests
{
    private static readonly Guid ServerId = new("6f1c2b0e-4d3a-4f5e-9a8b-1c2d3e4f5a6b");

    private static readonly Guid OtherId = new("0a9b8c7d-6e5f-4a3b-8c2d-1e0f9a8b7c6d");

    private readonly FakeModLibraryFile _store = new();

    private LocalCollectionStore Create(params SavedWorkshopEntry[] items) =>
        TestWorkshop.LocalCollectionStore(
            _store,
            new SavedLocalCollection(ServerId, "Server", items),
            new SavedLocalCollection(OtherId, "Other", [new SavedWorkshopEntry(2001, "Weather")]));

    private static IEnumerable<ulong> Ids(LocalCollectionStore collections, Guid id) => collections.Find(id)!.Items.Select(item => item.Id);

    [Fact]
    public void Load_KeepsTheSavedCollectionsInOrder()
    {
        var collections = Create(new SavedWorkshopEntry(1001, "Core"), new SavedWorkshopEntry(1002, null));

        Assert.Equal(["Server", "Other"], collections.All.Select(collection => collection.Name));
        Assert.Equal([new WorkshopEntry(1001, "Core"), new WorkshopEntry(1002, null)], collections.Find(ServerId)!.Items);
    }

    [Fact]
    public void Create_AppendsItWithEachIdOnceAndSavesAll()
    {
        var collections = Create();

        var created = collections.Create("  New list ", [new(1003, "Cars"), new(1001, null), new(1003, "Repeated")]);

        Assert.Equal("New list", created.Name);
        Assert.NotEqual(Guid.Empty, created.Id);
        Assert.Equal([new WorkshopEntry(1003, "Cars"), new WorkshopEntry(1001, null)], created.Items);
        Assert.Equal(["Server", "Other", "New list"], collections.All.Select(collection => collection.Name));
        Assert.Equal(
            new SavedLocalCollection(created.Id, "New list", [new SavedWorkshopEntry(1003, "Cars"), new SavedWorkshopEntry(1001, null)]),
            Assert.Single(_store.SavedLocalCollections)[2]);
    }

    [Theory]
    [InlineData("Other")]
    [InlineData(" Other ")]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("Two\nlines")]
    public void Create_NameTakenOrInvalid_IsABugAndTryCreateChangesNothing(string name)
    {
        var collections = Create();

        Assert.Throws<ArgumentException>(() => collections.Create(name, []));
        Assert.Null(collections.TryCreate(name, []));
        Assert.Equal(2, collections.All.Count);
        Assert.Empty(_store.SavedLocalCollections);
    }

    [Fact]
    public void IsNameTaken_IgnoresTheCollectionBeingRenamedAndSurroundingSpace()
    {
        var collections = Create();

        Assert.True(collections.IsNameTaken(" Other"));
        Assert.False(collections.IsNameTaken("Other", OtherId));
        Assert.False(collections.IsNameTaken("other"));
    }

    [Fact]
    public void Rename_KeepsTheIdAndItems()
    {
        var collections = Create(new SavedWorkshopEntry(1001, "Core"));

        collections.Rename(ServerId, "Server B42 ");

        var renamed = collections.Find(ServerId)!;
        Assert.Equal("Server B42", renamed.Name);
        Assert.Equal([1001UL], Ids(collections, ServerId));
        Assert.Single(_store.SavedLocalCollections);
    }

    [Fact]
    public void Rename_SameName_SavesNothingAndRaisesNothing()
    {
        var collections = Create();
        var raised = 0;
        collections.Changed += (_, _) => raised++;

        collections.Rename(ServerId, "Server");

        Assert.Empty(_store.SavedLocalCollections);
        Assert.Equal(0, raised);
    }

    [Fact]
    public void Delete_RemovesOnlyThatCollection()
    {
        var collections = Create();

        collections.Delete(ServerId);

        Assert.Equal([OtherId], collections.All.Select(collection => collection.Id));
        Assert.Null(collections.Find(ServerId));
        Assert.Equal([OtherId], Assert.Single(_store.SavedLocalCollections).Select(collection => collection.Id));
    }

    [Fact]
    public void Add_AppendsOnlyNewIdsInOrderAndCountsThem()
    {
        var collections = Create(new SavedWorkshopEntry(1001, "Core"));

        var added = collections.Add(ServerId, [new(1003, "Cars"), new(1001, "Other title"), new(1002, null), new(1003, null)]);

        Assert.Equal(2, added);
        Assert.Equal([new WorkshopEntry(1001, "Core"), new WorkshopEntry(1003, "Cars"), new WorkshopEntry(1002, null)], collections.Find(ServerId)!.Items);
        Assert.Single(_store.SavedLocalCollections);
    }

    [Fact]
    public void Add_AllListedAlready_SavesNothingAndRaisesNothing()
    {
        var collections = Create(new SavedWorkshopEntry(1001, "Core"));
        var raised = 0;
        collections.Changed += (_, _) => raised++;

        Assert.Equal(0, collections.Add(ServerId, [new(1001, null)]));
        Assert.Equal(0, collections.Add(Guid.NewGuid(), [new(1005, null)]));

        Assert.Empty(_store.SavedLocalCollections);
        Assert.Equal(0, raised);
    }

    [Fact]
    public void Replace_SetsTheItemsInOrderEachOnceKeepingKnownTitles()
    {
        var collections = Create(new SavedWorkshopEntry(1001, "Core"), new SavedWorkshopEntry(1002, "Maps"));

        collections.Replace(ServerId, [new(1003, null), new(1001, null), new(1003, "Repeated")]);

        Assert.Equal([new WorkshopEntry(1003, null), new WorkshopEntry(1001, "Core")], collections.Find(ServerId)!.Items);
        Assert.Equal([2001UL], Ids(collections, OtherId));
        Assert.Single(_store.SavedLocalCollections);
    }

    [Fact]
    public void Remove_DropsTheItemFromThatCollectionOnly()
    {
        var collections = Create(new SavedWorkshopEntry(2001, "Weather"), new SavedWorkshopEntry(1001, "Core"));

        collections.Remove(ServerId, 2001);

        Assert.Equal([1001UL], Ids(collections, ServerId));
        Assert.Equal([2001UL], Ids(collections, OtherId));
        Assert.Single(_store.SavedLocalCollections);
    }

    [Theory]
    [InlineData(0, 2, new ulong[] { 1002, 1003, 1001 })]
    [InlineData(2, 0, new ulong[] { 1003, 1001, 1002 })]
    [InlineData(1, 2, new ulong[] { 1001, 1003, 1002 })]
    public void Move_ReordersTheItems(int from, int to, ulong[] expected)
    {
        var collections = Create(new SavedWorkshopEntry(1001, null), new SavedWorkshopEntry(1002, null), new SavedWorkshopEntry(1003, null));

        collections.Move(ServerId, from, to);

        Assert.Equal(expected, Ids(collections, ServerId));
        Assert.Single(_store.SavedLocalCollections);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(-1, 0)]
    [InlineData(0, 2)]
    public void Move_SamePlaceOrOutOfRange_ChangesNothing(int from, int to)
    {
        var collections = Create(new SavedWorkshopEntry(1001, null), new SavedWorkshopEntry(1002, null));

        collections.Move(ServerId, from, to);

        Assert.Equal([1001UL, 1002], Ids(collections, ServerId));
        Assert.Empty(_store.SavedLocalCollections);
    }

    [Fact]
    public void SeenTitles_KeepsNewTitlesOfListedIdsInEveryCollectionAndSavesOnce()
    {
        var collections = Create(new SavedWorkshopEntry(1001, null), new SavedWorkshopEntry(2001, "Old"));
        var raised = 0;
        collections.Changed += (_, _) => raised++;
        var titles = new WorkshopTitles(TestWorkshop.History(), TestWorkshop.UserCollections(), TestWorkshop.Favorites(), collections);

        titles.SeenItems([new(1001, "Core"), new(2001, "Weather Pack"), new(3001, "Not listed"), new(1001, null)]);

        Assert.Equal([new WorkshopEntry(1001, "Core"), new WorkshopEntry(2001, "Weather Pack")], collections.Find(ServerId)!.Items);
        Assert.Equal([new WorkshopEntry(2001, "Weather Pack")], collections.Find(OtherId)!.Items);
        Assert.Single(_store.SavedLocalCollections);
        Assert.Equal(1, raised);
    }

    [Fact]
    public void SeenTitles_NothingNew_SavesNothing()
    {
        var collections = Create(new SavedWorkshopEntry(1001, "Core"));
        var titles = new WorkshopTitles(TestWorkshop.History(), TestWorkshop.UserCollections(), TestWorkshop.Favorites(), collections);

        titles.SeenItems([new(1001, "Core"), new(1001, null)]);
        titles.SeenCollections([new(1001, "A collection title")]);

        Assert.Empty(_store.SavedLocalCollections);
    }

    [Fact]
    public void EveryChange_RaisesChangedOnce()
    {
        var collections = Create(new SavedWorkshopEntry(1001, null), new SavedWorkshopEntry(1002, null));
        var raised = 0;
        collections.Changed += (_, _) => raised++;

        var created = collections.Create("New", []);
        collections.Rename(created.Id, "Renamed");
        collections.Add(created.Id, [new(1005, null)]);
        collections.Remove(created.Id, 1005);
        collections.Move(ServerId, 0, 1);
        collections.Delete(created.Id);

        Assert.Equal(6, raised);
        Assert.Equal(6, _store.SavedLocalCollections.Count);
    }
}
