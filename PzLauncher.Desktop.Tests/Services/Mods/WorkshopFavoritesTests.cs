using PzLauncher.Desktop.Services.Mods;
using PzLauncher.Desktop.Services.Preferences;
using PzLauncher.Desktop.Tests.TestSupport;

namespace PzLauncher.Desktop.Tests.Services.Mods;

public class WorkshopFavoritesTests
{
    private readonly FakePreferenceStore _store = new();

    private static WorkshopTitles Titles(WorkshopFavorites favorites) => new(TestWorkshop.History(), TestWorkshop.UserCollections(), favorites, TestWorkshop.LocalCollectionStore());

    private WorkshopFavorites Create(params SavedWorkshopEntry[] items) =>
        TestWorkshop.Favorites(_store, new SavedWorkshopFavorites([new SavedWorkshopEntry(9000, "Pack")], items));

    [Fact]
    public void Toggle_NotStarred_AddsItOnTopAndSavesBothLists()
    {
        var favorites = Create(new SavedWorkshopEntry(1001, "Core"));

        favorites.Toggle(WorkshopFavoriteKind.Item, 1002, "Maps");

        Assert.True(favorites.IsFavorite(WorkshopFavoriteKind.Item, 1002));
        Assert.False(favorites.IsFavorite(WorkshopFavoriteKind.Collection, 1002));
        Assert.Equal(
            new SavedWorkshopFavorites([new SavedWorkshopEntry(9000, "Pack")], [new SavedWorkshopEntry(1002, "Maps"), new SavedWorkshopEntry(1001, "Core")]),
            Assert.Single(_store.SavedWorkshopFavorites));
    }

    [Fact]
    public void Toggle_Starred_RemovesItAndSaves()
    {
        var favorites = Create();

        favorites.Toggle(WorkshopFavoriteKind.Collection, 9000, "Pack");

        Assert.Empty(favorites.Collections);
        Assert.Equal(SavedWorkshopFavorites.None, Assert.Single(_store.SavedWorkshopFavorites));
    }

    [Fact]
    public void Toggle_RaisesChangedOnce()
    {
        var favorites = Create();
        var raised = 0;
        favorites.Changed += (_, _) => raised++;

        favorites.Toggle(WorkshopFavoriteKind.Item, 1001, null);

        Assert.Equal(1, raised);
    }

    [Fact]
    public void SeenTitles_KeepsNewTitlesOfStarredIdsInPlaceAndSavesOnce()
    {
        var favorites = Create(new SavedWorkshopEntry(1001, null), new SavedWorkshopEntry(1002, "Old"), new SavedWorkshopEntry(1003, "Same"));
        var raised = 0;
        favorites.Changed += (_, _) => raised++;

        Titles(favorites).SeenItems([new(1001, "Core"), new(1002, "New"), new(1003, "Same"), new(1004, "Not starred"), new(1002, null)]);

        Assert.Equal([new WorkshopEntry(1001, "Core"), new WorkshopEntry(1002, "New"), new WorkshopEntry(1003, "Same")], favorites.Items);
        Assert.Single(_store.SavedWorkshopFavorites);
        Assert.Equal(1, raised);
    }

    [Fact]
    public void SeenTitles_NothingNew_SavesNothingAndRaisesNothing()
    {
        var favorites = Create(new SavedWorkshopEntry(1001, "Core"));
        var raised = 0;
        favorites.Changed += (_, _) => raised++;

        Titles(favorites).SeenItems([new(1001, "Core"), new(1001, null)]);
        Titles(favorites).SeenCollections([new(1001, "Core")]);

        Assert.Empty(_store.SavedWorkshopFavorites);
        Assert.Equal(0, raised);
    }

    [Fact]
    public void Add_AddsOnlyNewIdsOnTopInOrderAndKeepsTheOthers()
    {
        var favorites = Create(new SavedWorkshopEntry(1001, "Core"));

        var added = favorites.Add(
            [new(9000, "Other title"), new(9100, "Vehicles Pack")],
            [new(1002, null), new(1001, null), new(1003, "Weather"), new(1002, "Repeated")]);

        Assert.Equal((1, 2), added);
        Assert.Equal([new WorkshopEntry(9100, "Vehicles Pack"), new WorkshopEntry(9000, "Pack")], favorites.Collections);
        Assert.Equal(
            [new WorkshopEntry(1002, null), new WorkshopEntry(1003, "Weather"), new WorkshopEntry(1001, "Core")],
            favorites.Items);
        Assert.Single(_store.SavedWorkshopFavorites);
    }

    [Fact]
    public void Add_AllStarredAlready_AddsNothingAndSavesNothing()
    {
        var favorites = Create(new SavedWorkshopEntry(1001, "Core"));

        var added = favorites.Add([new(9000, null)], [new(1001, null)]);

        Assert.Equal((0, 0), added);
        Assert.Empty(_store.SavedWorkshopFavorites);
    }
}
