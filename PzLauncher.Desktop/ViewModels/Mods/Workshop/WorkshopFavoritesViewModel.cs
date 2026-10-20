using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PzLauncher.Core.Common;
using PzLauncher.Core.Steam.Workshop;
using PzLauncher.Core.Steam.Workshop.Actions;
using PzLauncher.Desktop.Lang;
using PzLauncher.Desktop.Localization;
using PzLauncher.Desktop.Services.Mods;
using PzLauncher.Desktop.Services.Preferences;
using PzLauncher.Desktop.Services.Shell;

namespace PzLauncher.Desktop.ViewModels.Mods.Workshop;

/// <summary>
/// The Steam Workshop collections and items starred in the launcher: the items listed with their state for the account Steam signs in with
/// and the install in use, and both shared and imported as text.
/// </summary>
public sealed partial class WorkshopFavoritesViewModel : WorkshopItemListSectionViewModel
{
    private readonly IClipboardService _clipboard;
    private readonly LocalizedText _favoritesStatus;

    internal WorkshopFavoritesViewModel(
        ModsViewModel page,
        IWorkshopService service,
        IWorkshopActionRunner runner,
        IDialogService dialogs,
        IClipboardService clipboard,
        IUrlLauncher urls,
        WorkshopFavorites favorites)
        : base(page, service, runner, dialogs, urls, favorites)
    {
        _clipboard = clipboard;
        _favoritesStatus = new LocalizedText(text => FavoritesStatusText = text);
        Collections = [];
        RefreshCollections();
    }

    /// <summary>The starred collections, most recent first.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCollections))]
    public partial IReadOnlyList<RememberedWorkshopCollectionRow> Collections { get; private set; }

    public bool HasCollections => Collections.Count > 0;

    /// <summary>What the last share or import did.</summary>
    [ObservableProperty] public partial string? FavoritesStatusText { get; private set; }

    [ObservableProperty] public partial bool IsImportingFavorites { get; private set; }

    protected override string GeneratedCollectionName => UiText.WorkshopFavorites_CollectionName;

    /// <summary>Lists the starred items again, so ones starred elsewhere since appear.</summary>
    internal Task ShowAsync() => LoadItemsAsync();

    internal override void RenderTexts()
    {
        base.RenderTexts();
        _favoritesStatus.Render();
    }

    [RelayCommand]
    private Task OpenCollectionAsync(RememberedWorkshopCollectionRow row) => Page.OpenWorkshopCollectionAsync(row.Id);

    [RelayCommand]
    private void UnstarCollection(RememberedWorkshopCollectionRow row)
    {
        if (IsFavoriteCollection(row.Id))
        {
            Favorites.Toggle(WorkshopFavoriteKind.Collection, row.Id, null);
        }
    }

    private bool CanShareFavorites() => Favorites.Collections.Count > 0 || Favorites.Items.Count > 0;

    [RelayCommand(CanExecute = nameof(CanShareFavorites))]
    private async Task ShareFavoritesAsync()
    {
        var collections = Favorites.Collections.Count;
        var items = Favorites.Items.Count;
        await _clipboard.SetTextAsync(
            WorkshopFavoritesShareText.Format(Favorites.Collections.Select(entry => entry.Id), Favorites.Items.Select(entry => entry.Id)));
        _favoritesStatus.Show(() => string.Format(CultureInfo.CurrentCulture, UiText.WorkshopFavorites_Shared, collections, items));
    }

    /// <summary>Adds the ids in pasted share text that aren't starred yet; ids without a section are a collection if the Web API knows one.</summary>
    [RelayCommand]
    private async Task ImportFavoritesAsync()
    {
        // Offered only when it holds an id, so unrelated clipboard text never shows up in the box.
        var clipboard = await _clipboard.GetTextAsync();
        var initial = clipboard is not null && WorkshopFavoritesShareText.Parse(clipboard).HasIds ? clipboard.Trim() : "";
        var prompt = new TextPrompt(
            UiText.WorkshopFavorites_ImportTitle,
            UiText.WorkshopFavorites_ImportMessage,
            initial,
            UiText.WorkshopFavorites_Import,
            text => WorkshopFavoritesShareText.Parse(text).HasIds ? null : UiText.WorkshopFavorites_ImportNothing,
            IsMultiline: true);
        if (await Dialogs.PromptTextAsync(prompt) is not TextPromptResult.Accepted accepted)
        {
            return;
        }

        var shared = WorkshopFavoritesShareText.Parse(accepted.Text);
        IsImportingFavorites = true;
        _favoritesStatus.Clear();
        try
        {
            List<SavedWorkshopEntry> collections = [.. shared.Collections.Select(id => new SavedWorkshopEntry(id, null))];
            List<SavedWorkshopEntry> items = [.. shared.Items.Select(id => new SavedWorkshopEntry(id, null))];
            var skipped = shared.SkippedLines;
            foreach (var id in shared.Unmarked.Where(id => !IsFavoriteCollection(id) && !IsFavoriteItem(id)))
            {
                // Nothing is guessed: an id the Web API can't classify is skipped.
                var collection = await Service.GetCollectionAsync(id);
                if (collection.IsSuccess)
                {
                    collections.Add(new SavedWorkshopEntry(id, collection.Value.Title));
                }
                else if (collection.Error.Code == ErrorCode.WorkshopCollectionNotFound)
                {
                    items.Add(new SavedWorkshopEntry(id, null));
                }
                else
                {
                    skipped++;
                }
            }

            var (addedCollections, addedItems) = Favorites.Add(collections, items);
            _favoritesStatus.Show(() => string.Format(CultureInfo.CurrentCulture, UiText.WorkshopFavorites_Imported, addedCollections, addedItems, skipped));
            await FillFavoriteTitlesAsync();
        }
        finally
        {
            IsImportingFavorites = false;
        }

        await LoadItemsAsync();
    }

    protected override void OnFavoritesChanged()
    {
        // An unstarred item leaves the list at once, without asking Steam or the Web API again.
        KeepItems(IsFavoriteItem);
        ShowFavorites(Rows);
        ShareFavoritesCommand.NotifyCanExecuteChanged();
        RefreshCollections();
    }

    protected override Task ShowAgainAsync() => LoadItemsAsync();

    /// <summary>The starred items: their latest versions from the Web API, merged with the subscriptions like an opened collection.</summary>
    private Task LoadItemsAsync() => RunLoadAsync(async version =>
    {
        List<ulong> ids = [.. Favorites.Items.Select(entry => entry.Id)];
        Dictionary<ulong, WorkshopItemDetails> latest = [];
        Error? detailsError = null;
        if (ids.Count > 0)
        {
            var details = await Service.CheckUpdatesAsync(ids);
            if (!IsLatest(version))
            {
                return;
            }

            if (details.IsSuccess)
            {
                latest = details.Value.ToDictionary(item => item.Id);
            }
            else
            {
                detailsError = details.Error;
            }
        }

        // The list as it is now: a star may have changed while the Web API answered.
        SetItems([.. Favorites.Items.Select(entry => new WorkshopCollectionItem(entry.Id, latest.GetValueOrDefault(entry.Id)))]);
        await MergeAsync(version);
        if (IsLatest(version) && detailsError is not null)
        {
            AddWarning(detailsError);
        }
    });

    // Titles for starred ids saved without one; a failed lookup leaves them as ids.
    private async Task FillFavoriteTitlesAsync()
    {
        List<ulong> collections = [.. Favorites.Collections.Where(entry => entry.Title is null).Select(entry => entry.Id)];
        List<ulong> items = [.. Favorites.Items.Where(entry => entry.Title is null).Select(entry => entry.Id)];
        if (collections.Count == 0 && items.Count == 0)
        {
            return;
        }

        var details = await Service.CheckUpdatesAsync([.. collections, .. items]);
        if (!details.IsSuccess)
        {
            return;
        }

        Favorites.UpdateTitles(
            WorkshopFavoriteKind.Collection, details.Value.Where(item => collections.Contains(item.Id)).Select(item => new SavedWorkshopEntry(item.Id, item.Title)));
        Favorites.UpdateTitles(
            WorkshopFavoriteKind.Item, details.Value.Where(item => items.Contains(item.Id)).Select(item => new SavedWorkshopEntry(item.Id, item.Title)));
    }

    private void RefreshCollections() =>
        Collections = [.. Favorites.Collections.Select(saved => RememberedWorkshopCollectionRow.Of(saved, isOpen: false, isFavorite: true))];
}
