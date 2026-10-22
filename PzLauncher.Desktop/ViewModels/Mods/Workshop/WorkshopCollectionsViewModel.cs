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
using PzLauncher.Desktop.Services.Shell;

namespace PzLauncher.Desktop.ViewModels.Mods.Workshop;

/// <summary>
/// A Steam Workshop collection opened by link, id, from the account's favorited and published ones or from the local favorites, with each
/// item's state for the account Steam signs in with and the install in use.
/// </summary>
public partial class WorkshopCollectionsViewModel : WorkshopItemListSectionViewModel
{
    private readonly WorkshopCollectionHistory _history;
    private readonly WorkshopUserCollectionsCache _userCache;
    private readonly LocalizedText _userUpdated;
    private readonly LocalizedText _userError;

    private WorkshopCollection? _collection;

    // The account whose collections Steam listed are shown; null until it's known.
    private uint? _account;

    // Steam is asked once per launcher session (the helper makes Steam show the game running); a skipped automatic fetch doesn't count.
    private bool _userCollectionsFetched;

    internal WorkshopCollectionsViewModel(
        ModsViewModel page,
        IWorkshopService service,
        IWorkshopActionRunner runner,
        IDialogService dialogs,
        IUrlLauncher urls,
        WorkshopCollectionHistory history,
        WorkshopUserCollectionsCache userCache,
        WorkshopFavorites favorites,
        WorkshopTitles titles,
        LocalCollections localCollections)
        : base(page, service, runner, dialogs, urls, favorites, titles, localCollections)
    {
        _history = history;
        _userCache = userCache;
        _userUpdated = new LocalizedText(text => UserCollectionsUpdatedText = text);
        _userError = new LocalizedText(text => UserCollectionsErrorText = text);
        Remembered = [];
        Favorited = [];
        Published = [];
        AddStateCommands(OpenCommand, RefreshUserCollectionsCommand, UnsubscribeAllCommand);
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
    public partial IReadOnlyList<WorkshopCollectionRow> Remembered { get; private set; }

    public bool HasRemembered => Remembered.Count > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFavoritedEmpty))]
    public partial IReadOnlyList<WorkshopCollectionRow> Favorited { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPublishedEmpty))]
    public partial IReadOnlyList<WorkshopCollectionRow> Published { get; private set; }

    /// <summary>Steam's answer is shown and lists none; before any answer the group is just empty.</summary>
    public bool IsFavoritedEmpty => UserCollections is not null && Favorited.Count == 0;

    /// <inheritdoc cref="IsFavoritedEmpty"/>
    public bool IsPublishedEmpty => UserCollections is not null && Published.Count == 0;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshUserCollectionsCommand))]
    public partial bool IsFetchingUserCollections { get; private set; }

    /// <summary>The fetch waits for an earlier Steam action from this launcher to end.</summary>
    [ObservableProperty] public partial bool IsWaitingForEarlierAction { get; private set; }

    /// <summary>When Steam gave the shown lists.</summary>
    [ObservableProperty] public partial string? UserCollectionsUpdatedText { get; private set; }

    /// <summary>Why the last fetch failed; cleared by a later success.</summary>
    [ObservableProperty] public partial string? UserCollectionsErrorText { get; private set; }

    /// <summary>The open collection's title, else its id; <c>null</c> when none is open.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCollection))]
    public partial string? Title { get; private set; }

    [ObservableProperty] public partial string? IdText { get; private set; }

    public bool HasCollection => Title is not null;

    public bool IsOpenCollectionFavorite => _collection is { } open && IsFavoriteCollection(open.Id);

    protected override string GeneratedCollectionName => _collection?.Title ?? "";

    private FetchedWorkshopUserCollections? UserCollections => _account is { } account ? _userCache.For(account) : null;

    /// <summary>The account's Steam collections (asked once per session), and the open collection's subscription states again.</summary>
    internal override Task ShowAsync() => Task.WhenAll(ShowUserCollectionsAsync(), ShowAgainAsync());

    /// <summary>Opens the open collection again from the Web API.</summary>
    internal override Task RefreshAsync() => _collection is { } collection ? LoadCollectionAsync(collection.Id) : Task.CompletedTask;

    /// <summary>Opens the collection from the Web API and remembers it.</summary>
    internal Task LoadCollectionAsync(ulong id) => RunLoadAsync(async version =>
    {
        ShownStatus.Clear();
        var result = await Service.GetCollectionAsync(id);
        if (!IsLatest(version))
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

        _history.Remember(id, result.Value.Title);
        Titles.SeenCollections([new WorkshopEntry(id, result.Value.Title)]);
        SetCollection(result.Value);
        LinkText = "";
        await MergeAsync(version);
    });

    internal override void RenderTexts()
    {
        base.RenderTexts();
        _userUpdated.Render();
        _userError.Render();
        OnPropertyChanged(nameof(LinkErrorText));
    }

    private bool CanOpen() => !IsLoading && WorkshopLinks.TryParseItemId(LinkText, out _);

    [RelayCommand(CanExecute = nameof(CanOpen))]
    private Task OpenAsync() => WorkshopLinks.TryParseItemId(LinkText, out var id) ? LoadCollectionAsync(id) : Task.CompletedTask;

    private bool CanRefreshUserCollections() => Page.Install is not null && !Page.IsGameRunning && !IsActionRunning && !IsFetchingUserCollections;

    [RelayCommand(CanExecute = nameof(CanRefreshUserCollections))]
    private Task RefreshUserCollectionsAsync() => FetchUserCollectionsAsync();

    [RelayCommand]
    private void Forget(WorkshopCollectionRow row)
    {
        _history.Forget(row.Id);
        RefreshCollectionLists();
    }

    // Starring is a local write, never Steam's: available while a game or a Steam action runs.
    private bool CanToggleOpenCollectionFavorite() => HasCollection;

    [RelayCommand(CanExecute = nameof(CanToggleOpenCollectionFavorite))]
    private void ToggleOpenCollectionFavorite()
    {
        if (_collection is { } open)
        {
            Favorites.Toggle(WorkshopFavoriteKind.Collection, open.Id, open.Title);
        }
    }

    private bool CanOpenCollectionInSteam() => HasCollection;

    [RelayCommand(CanExecute = nameof(CanOpenCollectionInSteam))]
    private Task OpenCollectionInSteamAsync() =>
        _collection is { } open ? OpenInSteamAsync(open.Id) : Task.CompletedTask;

    private bool CanSaveAsLocalCollection() => HasCollection;

    /// <summary>A local collection of the open collection's items, in collection order.</summary>
    [RelayCommand(CanExecute = nameof(CanSaveAsLocalCollection))]
    private async Task SaveAsLocalCollectionAsync()
    {
        if (_collection is not { } open)
        {
            return;
        }

        var initial = open.Title is { } openTitle && LocalCollectionName.IsValid(openTitle) ? openTitle.Trim() : "";
        if (await LocalCollectionPrompts.AskNameAsync(
                Dialogs, LocalCollections, UiText.LocalCollections_SaveAsTitle, initial, UiText.ModCollections_Create) is not { } name)
        {
            return;
        }

        var saved = LocalCollections.Create(name, [.. open.Items.Select(item => new WorkshopEntry(item.Id, item.Details?.Title is { Length: > 0 } itemTitle ? itemTitle : null))]);
        var count = saved.Items.Count;
        ShownStatus.Show(() => string.Format(CultureInfo.CurrentCulture, UiText.LocalCollections_SavedAs, count, name));
    }

    private bool CanUnsubscribeAll() => CanRunActions && SubscribedCount > 0;

    [RelayCommand(CanExecute = nameof(CanUnsubscribeAll))]
    private async Task UnsubscribeAllAsync()
    {
        List<WorkshopItemRow> subscribed = [.. Rows.Where(row => row.Item.State != WorkshopItemState.NotSubscribed)];
        if (subscribed.Count > 0 && await WorkshopActionFlow.ConfirmUnsubscribeAsync(Dialogs, subscribed))
        {
            await RunAsync(WorkshopAction.Unsubscribe, subscribed);
        }
    }

    protected override void OnFavoritesChanged()
    {
        ShowRowFavorites();
        RefreshCollectionLists();
    }

    protected override Task ShowAgainAsync() => _collection is null ? Task.CompletedTask : RunLoadAsync(MergeAsync);

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

        SetUserCollections(account.IsSuccess ? account.Value.AccountId : null);
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
            var progress = new ContextProgress<WorkshopActionEvent>(report => IsWaitingForEarlierAction = report is WorkshopActionWaiting);
            var result = await Runner.ListCollectionsAsync(install.RootPath, progress);
            if (result.IsSuccess)
            {
                _userError.Clear();
                _userCache.Save(result.Value);
                SetUserCollections(result.Value.AccountId);
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
            IsWaitingForEarlierAction = false;
            IsFetchingUserCollections = false;
        }
    }

    private void SetUserCollections(uint? account)
    {
        _account = account;
        if (UserCollections is { } shown)
        {
            var fetchedAt = shown.FetchedAt.LocalDateTime.ToString("g", CultureInfo.CurrentCulture);
            _userUpdated.Show(() => string.Format(CultureInfo.CurrentCulture, UiText.WorkshopCollections_UpdatedFormat, fetchedAt));
            Titles.SeenCollections(shown.Collections.Favorited.Concat(shown.Collections.Published).Select(summary => new WorkshopEntry(summary.Id, summary.Title)));
        }
        else
        {
            _userUpdated.Clear();
        }

        RefreshCollectionLists();
    }

    private void SetCollection(WorkshopCollection? collection)
    {
        _collection = collection;
        SetItems(collection?.Items);
        Title = collection is null ? null : collection.Title ?? collection.Id.ToString(CultureInfo.InvariantCulture);
        IdText = collection?.Id.ToString(CultureInfo.InvariantCulture);
        OpenCollectionInSteamCommand.NotifyCanExecuteChanged();
        ToggleOpenCollectionFavoriteCommand.NotifyCanExecuteChanged();
        SaveAsLocalCollectionCommand.NotifyCanExecuteChanged();
        RefreshCollectionLists();
    }

    private void RefreshCollectionLists()
    {
        var open = _collection?.Id;
        Remembered = [.. _history.Items.Select(entry => new WorkshopCollectionRow(entry.Id, entry.Title, null, entry.Id == open, IsFavoriteCollection(entry.Id)))];
        Favorited = UserRows(UserCollections?.Collections.Favorited ?? [], open);
        Published = UserRows(UserCollections?.Collections.Published ?? [], open);
        OnPropertyChanged(nameof(IsOpenCollectionFavorite));
    }

    private List<WorkshopCollectionRow> UserRows(IReadOnlyList<WorkshopCollectionSummary> collections, ulong? open) =>
    [
        .. collections.Select(collection => new WorkshopCollectionRow(
            collection.Id, collection.Title, collection.ChildCount, collection.Id == open, IsFavoriteCollection(collection.Id))),
    ];
}
