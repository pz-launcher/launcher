using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PzLauncher.Core.Common;
using PzLauncher.Core.Mod;
using PzLauncher.Core.Steam.Workshop;
using PzLauncher.Core.Steam.Workshop.Actions;
using PzLauncher.Desktop.Lang;
using PzLauncher.Desktop.Localization;
using PzLauncher.Desktop.Services.Mods;
using PzLauncher.Desktop.Services.Preferences;
using PzLauncher.Desktop.Services.Shell;

namespace PzLauncher.Desktop.ViewModels.Mods.Workshop;

/// <summary>
/// A Steam Workshop collection opened by link, id, from the account's favorited and published ones or from the local favorites, or the
/// locally starred items as one list; with each item's state for the account Steam signs in with and the install in use.
/// </summary>
public partial class WorkshopCollectionsViewModel : WorkshopSectionViewModel
{
    private readonly WorkshopCollectionHistory _history;
    private readonly WorkshopUserCollectionsCache _userCache;
    private readonly IClipboardService _clipboard;
    private readonly LocalizedText _userUpdated;
    private readonly LocalizedText _userError;
    private readonly LocalizedText _favoritesStatus;

    // For the starred items, a stand-in with id 0 that lists them.
    private WorkshopCollection? _collection;
    private SavedWorkshopUserCollections? _userCollections;

    // Steam is asked once per launcher session (the helper makes Steam show the game running); a skipped automatic fetch doesn't count.
    private bool _userCollectionsFetched;

    // A newer load supersedes one still in flight.
    private int _loadVersion;

    internal WorkshopCollectionsViewModel(
        ModsViewModel page,
        IWorkshopService service,
        IWorkshopActionRunner runner,
        IDialogService dialogs,
        IClipboardService clipboard,
        IUrlLauncher urls,
        WorkshopCollectionHistory history,
        WorkshopUserCollectionsCache userCache,
        WorkshopFavorites favorites)
        : base(page, service, runner, dialogs, urls, favorites)
    {
        _history = history;
        _userCache = userCache;
        _clipboard = clipboard;
        _userUpdated = new LocalizedText(text => UserCollectionsUpdatedText = text);
        _userError = new LocalizedText(text => UserCollectionsErrorText = text);
        _favoritesStatus = new LocalizedText(text => FavoritesStatusText = text);
        Rows = [];
        Remembered = [];
        Favorited = [];
        Published = [];
        FavoriteCollections = [];
        RefreshCollectionLists();
    }

    /// <summary>A collection link or id to open.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LinkErrorText))]
    [NotifyCanExecuteChangedFor(nameof(OpenCommand))]
    public partial string LinkText { get; set; } = "";

    /// <summary>Text that is neither a link nor an id; <c>null</c> when empty or valid.</summary>
    public string? LinkErrorText => LinkText.Trim().Length > 0 && !WorkshopLinks.TryParseItemId(LinkText, out _) ? UiText.Workshop_LinkInvalid : null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRemembered))]
    public partial IReadOnlyList<RememberedWorkshopCollectionRow> Remembered { get; private set; }

    public bool HasRemembered => Remembered.Count > 0;

    /// <summary>The locally starred collections, most recent first.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFavoriteCollections))]
    public partial IReadOnlyList<RememberedWorkshopCollectionRow> FavoriteCollections { get; private set; }

    public bool HasFavoriteCollections => FavoriteCollections.Count > 0;

    public int FavoriteItemCount => Favorites.Items.Count;

    /// <summary>What the last share or import did.</summary>
    [ObservableProperty] public partial string? FavoritesStatusText { get; private set; }

    [ObservableProperty] public partial bool IsImportingFavorites { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFavoritedEmpty))]
    public partial IReadOnlyList<WorkshopUserCollectionRow> Favorited { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPublishedEmpty))]
    public partial IReadOnlyList<WorkshopUserCollectionRow> Published { get; private set; }

    /// <summary>Steam's answer is shown and lists none; before any answer the group is just empty.</summary>
    public bool IsFavoritedEmpty => _userCollections is not null && Favorited.Count == 0;

    /// <inheritdoc cref="IsFavoritedEmpty"/>
    public bool IsPublishedEmpty => _userCollections is not null && Published.Count == 0;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshUserCollectionsCommand))]
    public partial bool IsFetchingUserCollections { get; private set; }

    /// <summary>When Steam gave the shown lists.</summary>
    [ObservableProperty] public partial string? UserCollectionsUpdatedText { get; private set; }

    /// <summary>Why the last fetch failed; cleared by a later success.</summary>
    [ObservableProperty] public partial string? UserCollectionsErrorText { get; private set; }

    /// <summary>The open collection's items in collection order, or the starred items most recent first.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(ItemCount), nameof(IsCollectionEmpty), nameof(IsFavoriteItemsEmpty), nameof(MissingCount), nameof(SubscribedCount), nameof(OutdatedCount))]
    public partial IReadOnlyList<WorkshopItemRow> Rows { get; private set; }

    /// <summary>The open collection's title, else its id; <c>null</c> when none is open.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCollection), nameof(ShowsSteamCollection))]
    public partial string? Title { get; private set; }

    [ObservableProperty] public partial string? IdText { get; private set; }

    /// <summary>The section shows the starred items rather than a Steam collection.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsSteamCollection))]
    public partial bool IsFavoriteItemsView { get; private set; }

    public bool HasCollection => Title is not null;

    public bool ShowsSteamCollection => HasCollection && !IsFavoriteItemsView;

    public bool IsOpenCollectionFavorite => OpenCollectionId is { } id && Favorites.IsFavorite(WorkshopFavoriteKind.Collection, id);

    public int ItemCount => Rows.Count;

    public bool IsCollectionEmpty => !IsFavoriteItemsView && _collection is { Items.Count: 0 };

    public bool IsFavoriteItemsEmpty => IsFavoriteItemsView && _collection is { Items.Count: 0 };

    public int MissingCount => Rows.Count(row => row.Item.State == WorkshopItemState.NotSubscribed);

    public int SubscribedCount => Rows.Count(row => row.Item.State != WorkshopItemState.NotSubscribed);

    public int OutdatedCount => Rows.Count(row => row.Item.State == WorkshopItemState.NeedsUpdate);

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenCommand), nameof(GenerateCommand))]
    public partial bool IsLoading { get; private set; }

    /// <summary>Steam actions may start: a collection is open, none runs, and no game from this launcher does.</summary>
    public override bool CanRunActions => HasCollection && base.CanRunActions;

    private ulong? OpenCollectionId => IsFavoriteItemsView ? null : _collection?.Id;

    /// <summary>For the section being shown: the account's Steam collections (asked once per session), and the open collection's subscription states again.</summary>
    internal Task ShowAsync() => Task.WhenAll(ShowUserCollectionsAsync(), ShowCollectionAsync());

    /// <summary>Opens the open collection again from the Web API.</summary>
    internal Task RefreshAsync() =>
        IsFavoriteItemsView ? LoadFavoriteItemsAsync()
        : _collection is { } collection ? LoadCollectionAsync(collection.Id)
        : Task.CompletedTask;

    internal override void RenderTexts()
    {
        base.RenderTexts();
        _userUpdated.Render();
        _userError.Render();
        _favoritesStatus.Render();
        if (IsFavoriteItemsView)
        {
            Title = UiText.WorkshopFavorites_Items;
        }

        OnPropertyChanged(nameof(LinkErrorText));
    }

    private bool CanOpen() => !IsLoading && WorkshopLinks.TryParseItemId(LinkText, out _);

    [RelayCommand(CanExecute = nameof(CanOpen))]
    private Task OpenAsync() => WorkshopLinks.TryParseItemId(LinkText, out var id) ? LoadCollectionAsync(id) : Task.CompletedTask;

    [RelayCommand]
    private Task OpenRememberedAsync(RememberedWorkshopCollectionRow row) => LoadCollectionAsync(row.Id);

    [RelayCommand]
    private Task OpenUserCollectionAsync(WorkshopUserCollectionRow row) => LoadCollectionAsync(row.Id);

    [RelayCommand]
    private Task OpenFavoriteItemsAsync()
    {
        ShownStatus.Clear();
        return LoadFavoriteItemsAsync();
    }

    private bool CanRefreshUserCollections() => Page.Install is not null && !Page.IsGameRunning && !IsActionRunning && !IsFetchingUserCollections;

    [RelayCommand(CanExecute = nameof(CanRefreshUserCollections))]
    private Task RefreshUserCollectionsAsync() => FetchUserCollectionsAsync();

    [RelayCommand]
    private void Forget(RememberedWorkshopCollectionRow row)
    {
        _history.Forget(row.Id);
        RefreshCollectionLists();
    }

    // Starring is a local write, never Steam's: available while a game or a Steam action runs.
    private bool CanToggleOpenCollectionFavorite() => ShowsSteamCollection;

    [RelayCommand(CanExecute = nameof(CanToggleOpenCollectionFavorite))]
    private void ToggleOpenCollectionFavorite()
    {
        if (OpenCollectionId is { } id)
        {
            Favorites.Toggle(WorkshopFavoriteKind.Collection, id, _collection?.Title);
        }
    }

    [RelayCommand]
    private void ToggleCollectionFavorite(RememberedWorkshopCollectionRow row) =>
        Favorites.Toggle(WorkshopFavoriteKind.Collection, row.Id, KnownCollectionTitle(row.Id));

    [RelayCommand]
    private void ToggleUserCollectionFavorite(WorkshopUserCollectionRow row) =>
        Favorites.Toggle(WorkshopFavoriteKind.Collection, row.Id, KnownCollectionTitle(row.Id));

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

        if (IsFavoriteItemsView)
        {
            await LoadFavoriteItemsAsync();
        }
    }

    private bool CanOpenCollectionInSteam() => ShowsSteamCollection;

    [RelayCommand(CanExecute = nameof(CanOpenCollectionInSteam))]
    private Task OpenCollectionInSteamAsync() =>
        OpenCollectionId is { } id ? OpenInSteamAsync(id) : Task.CompletedTask;

    private bool CanSubscribeMissing() => CanRunActions && MissingCount > 0;

    [RelayCommand(CanExecute = nameof(CanSubscribeMissing))]
    private Task SubscribeMissingAsync() =>
        RunAsync(WorkshopAction.Subscribe, Rows.Where(row => row.Item.State == WorkshopItemState.NotSubscribed));

    // Not offered for the starred items: they are no collection to leave.
    private bool CanUnsubscribeAll() => ShowsSteamCollection && CanRunActions && SubscribedCount > 0;

    [RelayCommand(CanExecute = nameof(CanUnsubscribeAll))]
    private async Task UnsubscribeAllAsync()
    {
        List<WorkshopItemRow> subscribed = [.. Rows.Where(row => row.Item.State != WorkshopItemState.NotSubscribed)];
        if (subscribed.Count > 0 && await WorkshopActionFlow.ConfirmUnsubscribeAsync(Dialogs, subscribed))
        {
            await RunAsync(WorkshopAction.Unsubscribe, subscribed);
        }
    }

    private bool CanUpdateOutdated() => CanRunActions && OutdatedCount > 0;

    [RelayCommand(CanExecute = nameof(CanUpdateOutdated))]
    private Task UpdateOutdatedAsync() =>
        RunAsync(WorkshopAction.Update, Rows.Where(row => row.Item.State == WorkshopItemState.NeedsUpdate));

    private bool CanGenerate() => HasCollection && Rows.Count > 0 && Page.CanWrite && !IsLoading && !IsActionRunning;

    /// <summary>A launcher mod collection of the installed items' mods, in collection order.</summary>
    [RelayCommand(CanExecute = nameof(CanGenerate))]
    private async Task GenerateAsync()
    {
        if (_collection is not { } collection)
        {
            return;
        }

        List<WorkshopItemRow> used = [];
        List<string> skipped = [];
        foreach (var row in Rows)
        {
            if (SkipReason(row.Item) is { } reason)
            {
                skipped.Add(string.Format(CultureInfo.CurrentCulture, UiText.WorkshopCollections_SkippedFormat, WorkshopActionFlow.NameWithId(row.Item), reason));
            }
            else
            {
                used.Add(row);
            }
        }

        if (used.Count == 0)
        {
            await Dialogs.ShowErrorAsync(UiText.WorkshopCollections_GenerateNothing);
            return;
        }

        List<InstalledMod> candidates = [.. used.SelectMany(row => row.Item.Mods)];
        List<WorkshopModPickItem> picks =
        [
            .. used
                .Where(row => row.Item.Mods.Count > 1)
                .Select(row => new WorkshopModPickItem(
                    row.Id,
                    WorkshopActionFlow.NameWithId(row.Item),
                    [.. row.Item.Mods.Select(mod => new WorkshopModChoice(mod, Conflicts(mod, candidates)))])),
        ];
        if ((picks.Count > 0 || skipped.Count > 0) && !await Dialogs.PickWorkshopModsAsync(new WorkshopModPickViewModel(picks, skipped)))
        {
            return;
        }

        var chosen = picks.ToDictionary(pick => pick.Id);
        List<string> ids =
        [
            .. used
                .SelectMany(row => chosen.TryGetValue(row.Id, out var pick)
                    ? pick.Mods.Where(choice => choice.IsChecked).Select(choice => choice.Mod.Id)
                    : row.Item.Mods.Select(mod => mod.Id))
                .Distinct(StringComparer.Ordinal),
        ];
        if (ids.Count == 0)
        {
            await Dialogs.ShowErrorAsync(UiText.WorkshopCollections_GenerateNothing);
            return;
        }

        var initialName = IsFavoriteItemsView ? UiText.WorkshopFavorites_CollectionName : collection.Title ?? "";
        var prompt = ModCollectionPrompts.NamePrompt(UiText.WorkshopCollections_GenerateTitle, initialName, UiText.ModCollections_Create);
        if (await Page.Collections.AskNameAsync(prompt, original: null) is not { } name || !await Page.Collections.SaveAndSelectAsync(name, ids))
        {
            return;
        }

        var count = ids.Count;
        ShownStatus.Show(() => string.Format(CultureInfo.CurrentCulture, UiText.WorkshopCollections_Generated, name, count));

        // The items' usage by launcher collections changed.
        await ShowCollectionAsync();
    }

    protected override void NotifyActionCommands()
    {
        RefreshUserCollectionsCommand.NotifyCanExecuteChanged();
        SubscribeMissingCommand.NotifyCanExecuteChanged();
        UnsubscribeAllCommand.NotifyCanExecuteChanged();
        UpdateOutdatedCommand.NotifyCanExecuteChanged();
        GenerateCommand.NotifyCanExecuteChanged();
    }

    protected override void OnPageChanged(string? propertyName)
    {
        base.OnPageChanged(propertyName);
        if (propertyName == nameof(ModsViewModel.CanWrite))
        {
            GenerateCommand.NotifyCanExecuteChanged();
        }
    }

    protected override void OnFavoritesChanged()
    {
        // An unstarred item leaves the starred items' list at once, without asking Steam or the Web API again.
        if (IsFavoriteItemsView && _collection is { } shown && shown.Items.Any(entry => !IsFavoriteItem(entry.Id)))
        {
            _collection = shown with { Items = [.. shown.Items.Where(entry => IsFavoriteItem(entry.Id))] };
            Rows = [.. Rows.Where(row => IsFavoriteItem(row.Id))];
            NotifyActionCommands();
        }

        ShowFavorites(Rows);
        OnPropertyChanged(nameof(FavoriteItemCount));
        ShareFavoritesCommand.NotifyCanExecuteChanged();
        RefreshCollectionLists();
    }

    private async Task LoadCollectionAsync(ulong id)
    {
        var version = ++_loadVersion;
        IsLoading = true;
        ShownStatus.Clear();
        try
        {
            var result = await Service.GetCollectionAsync(id);
            if (version != _loadVersion)
            {
                return;
            }

            if (!result.IsSuccess)
            {
                SetCollection(null);
                SetRows([]);
                ShownError.Show(result.Error);
                return;
            }

            SetCollection(result.Value);
            _history.Remember(id, result.Value.Title);
            Favorites.UpdateTitles(WorkshopFavoriteKind.Collection, [new SavedWorkshopEntry(id, result.Value.Title)]);
            RefreshCollectionLists();
            LinkText = "";
            await MergeAsync(version);
        }
        finally
        {
            if (version == _loadVersion)
            {
                IsLoading = false;
            }
        }
    }

    /// <summary>The starred items as a collection: their latest versions from the Web API, merged with the subscriptions like an opened one.</summary>
    private async Task LoadFavoriteItemsAsync()
    {
        var version = ++_loadVersion;
        IsLoading = true;
        try
        {
            List<ulong> ids = [.. Favorites.Items.Select(entry => entry.Id)];
            Dictionary<ulong, WorkshopItemDetails> latest = [];
            Error? detailsError = null;
            if (ids.Count > 0)
            {
                var details = await Service.CheckUpdatesAsync(ids);
                if (version != _loadVersion)
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
            SetCollection(
                new WorkshopCollection(0, null, [.. Favorites.Items.Select(entry => new WorkshopCollectionItem(entry.Id, latest.GetValueOrDefault(entry.Id)))]),
                favoriteItems: true);
            await MergeAsync(version);
            if (version == _loadVersion && detailsError is not null)
            {
                AddWarning(detailsError);
            }
        }
        finally
        {
            if (version == _loadVersion)
            {
                IsLoading = false;
            }
        }
    }

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

    private Task ShowCollectionAsync() => IsFavoriteItemsView ? LoadFavoriteItemsAsync() : ShowSteamCollectionAsync();

    private async Task ShowSteamCollectionAsync()
    {
        if (_collection is null)
        {
            return;
        }

        var version = ++_loadVersion;
        IsLoading = true;
        try
        {
            await MergeAsync(version);
        }
        finally
        {
            if (version == _loadVersion)
            {
                IsLoading = false;
            }
        }
    }

    private async Task MergeAsync(int version)
    {
        if (_collection is null || Page.Install is not { } install)
        {
            return;
        }

        var result = await Service.GetSubscriptionsAsync(install.RootPath, Page.ExtraGameArgs(install));
        if (version != _loadVersion || _collection is not { } collection)
        {
            return;
        }

        if (!result.IsSuccess)
        {
            SetRows([]);
            ShownError.Show(result.Error);
            return;
        }

        ShownError.Clear();
        ShowWarnings(result.Value.Warnings);
        var subscribed = new Dictionary<ulong, WorkshopItem>();
        foreach (var item in result.Value.Items)
        {
            subscribed.TryAdd(item.Id, item);
        }

        SetRows([.. collection.Items.Select(entry => ItemOf(entry, subscribed))]);
    }

    private Task RunAsync(WorkshopAction action, IEnumerable<WorkshopItemRow> rows) =>
        RunActionAsync(action, WorkshopActionFlow.ActionItems(rows), () => Rows, ShowCollectionAsync);

    private async Task ShowUserCollectionsAsync()
    {
        if (_userCollectionsFetched)
        {
            return;
        }

        var account = await Service.GetCurrentAccountAsync();
        if (_userCollectionsFetched)
        {
            return;
        }

        SetUserCollections(account.IsSuccess ? _userCache.For(account.Value.AccountId) : null);
        if (CanRefreshUserCollections())
        {
            await FetchUserCollectionsAsync();
        }
    }

    private async Task FetchUserCollectionsAsync()
    {
        if (!CanRefreshUserCollections() || Page.Install is not { } install)
        {
            return;
        }

        _userCollectionsFetched = true;
        IsFetchingUserCollections = true;
        try
        {
            var result = await Runner.ListCollectionsAsync(install.RootPath);
            if (result.IsSuccess)
            {
                _userError.Clear();
                SetUserCollections(_userCache.Save(result.Value));
            }
            else
            {
                // Steam was never reached, so the next show may still ask on its own.
                _userCollectionsFetched = result.Error.Code != ErrorCode.WorkshopSteamNotRunning;
                _userError.Show(result.Error);
            }
        }
        finally
        {
            IsFetchingUserCollections = false;
        }
    }

    private void SetUserCollections(SavedWorkshopUserCollections? collections)
    {
        _userCollections = collections;
        if (collections is null)
        {
            _userUpdated.Clear();
        }
        else
        {
            var fetchedAt = collections.FetchedAt.LocalDateTime.ToString("g", CultureInfo.CurrentCulture);
            _userUpdated.Show(() => string.Format(CultureInfo.CurrentCulture, UiText.WorkshopCollections_UpdatedFormat, fetchedAt));
        }

        RefreshCollectionLists();
        Favorites.UpdateTitles(WorkshopFavoriteKind.Collection, UserSummaries().Select(summary => new SavedWorkshopEntry(summary.Id, summary.Title)));
    }

    // The collection's own details fill in a title the subscription list hasn't fetched.
    private static WorkshopItem ItemOf(WorkshopCollectionItem entry, IReadOnlyDictionary<ulong, WorkshopItem> subscribed) =>
        subscribed.TryGetValue(entry.Id, out var item)
            ? item.Latest is null && entry.Details is not null ? item with { Latest = entry.Details } : item
            : new WorkshopItem(entry.Id, default, false, null, entry.Details, [], [], WorkshopItemState.NotSubscribed);

    private static string? SkipReason(WorkshopItem item) =>
        item.State == WorkshopItemState.NotSubscribed ? DisplayNames.Of(WorkshopItemState.NotSubscribed)
        : item.Installed is null ? DisplayNames.Of(WorkshopItemState.NotInstalled)
        : item.Mods.Count == 0 ? UiText.WorkshopCollections_SkippedNoMods
        : null;

    private static List<string> Conflicts(InstalledMod mod, IReadOnlyList<InstalledMod> candidates) =>
    [
        .. candidates
            .Where(other => other.Id != mod.Id
                && (mod.Incompatible.Contains(other.Id, StringComparer.Ordinal) || other.Incompatible.Contains(mod.Id, StringComparer.Ordinal)))
            .Select(other => other.Id)
            .Distinct(StringComparer.Ordinal),
    ];

    private string? KnownCollectionTitle(ulong id) =>
        Favorites.Collections.FirstOrDefault(entry => entry.Id == id)?.Title
        ?? _history.Items.FirstOrDefault(entry => entry.Id == id)?.Title
        ?? UserSummaries().FirstOrDefault(summary => summary.Id == id)?.Title;

    private IEnumerable<WorkshopCollectionSummary> UserSummaries() =>
        _userCollections is { } saved ? saved.Collections.Favorited.Concat(saved.Collections.Published) : [];

    /// <param name="favoriteItems">The collection is the stand-in for the starred items.</param>
    private void SetCollection(WorkshopCollection? collection, bool favoriteItems = false)
    {
        _collection = collection;
        IsFavoriteItemsView = collection is not null && favoriteItems;
        Title = collection is null ? null
            : favoriteItems ? UiText.WorkshopFavorites_Items
            : collection.Title ?? collection.Id.ToString(CultureInfo.InvariantCulture);
        IdText = OpenCollectionId?.ToString(CultureInfo.InvariantCulture);
        OnPropertyChanged(nameof(CanRunActions));
        OpenCollectionInSteamCommand.NotifyCanExecuteChanged();
        ToggleOpenCollectionFavoriteCommand.NotifyCanExecuteChanged();
        RefreshCollectionLists();
    }

    private void SetRows(IReadOnlyList<WorkshopItem> items)
    {
        Rows = [.. items.Select(item => new WorkshopItemRow(item, false, IsFavoriteItem(item.Id), _ => { }))];
        NotifyActionCommands();
        RememberTitles(Rows);
    }

    private void RefreshCollectionLists()
    {
        var open = OpenCollectionId;
        FavoriteCollections = [.. Favorites.Collections.Select(saved => new RememberedWorkshopCollectionRow(saved.Id, TitleOrId(saved), saved.Id == open, true))];
        Remembered = [.. _history.Items.Select(saved => new RememberedWorkshopCollectionRow(saved.Id, TitleOrId(saved), saved.Id == open, IsFavoriteCollection(saved.Id)))];
        Favorited = UserRows(_userCollections?.Collections.Favorited ?? [], open);
        Published = UserRows(_userCollections?.Collections.Published ?? [], open);
        OnPropertyChanged(nameof(IsOpenCollectionFavorite));
    }

    private List<WorkshopUserCollectionRow> UserRows(IReadOnlyList<WorkshopCollectionSummary> collections, ulong? open) =>
    [
        .. collections.Select(collection => new WorkshopUserCollectionRow(
            collection.Id,
            collection.Title ?? collection.Id.ToString(CultureInfo.InvariantCulture),
            collection.ChildCount,
            collection.Id == open,
            IsFavoriteCollection(collection.Id))),
    ];

    private bool IsFavoriteCollection(ulong id) => Favorites.IsFavorite(WorkshopFavoriteKind.Collection, id);

    private static string TitleOrId(SavedWorkshopEntry saved) => saved.Title ?? saved.Id.ToString(CultureInfo.InvariantCulture);
}
