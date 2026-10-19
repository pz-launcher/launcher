using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PzLauncher.Core.Mod;
using PzLauncher.Core.Steam.Workshop;
using PzLauncher.Core.Steam.Workshop.Actions;
using PzLauncher.Desktop.Lang;
using PzLauncher.Desktop.Localization;
using PzLauncher.Desktop.Services.Mods;
using PzLauncher.Desktop.Services.Shell;

namespace PzLauncher.Desktop.ViewModels.Mods.Workshop;

/// <summary>A Steam Workshop collection opened by link or id, with each item's state for the account Steam signs in with and the install in use.</summary>
public partial class WorkshopCollectionsViewModel : WorkshopSectionViewModel
{
    private readonly WorkshopCollectionHistory _history;
    private WorkshopCollection? _collection;

    // A newer load supersedes one still in flight.
    private int _loadVersion;

    internal WorkshopCollectionsViewModel(
        ModsViewModel page,
        IWorkshopService service,
        IWorkshopActionRunner runner,
        IDialogService dialogs,
        IUrlLauncher urls,
        WorkshopCollectionHistory history)
        : base(page, service, runner, dialogs, urls)
    {
        _history = history;
        Rows = [];
        Remembered = [];
        RefreshRemembered();
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

    /// <summary>The open collection's items in collection order.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ItemCount), nameof(IsCollectionEmpty), nameof(MissingCount), nameof(SubscribedCount), nameof(OutdatedCount))]
    public partial IReadOnlyList<WorkshopItemRow> Rows { get; private set; }

    /// <summary>The open collection's title, else its id; <c>null</c> when none is open.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCollection))]
    public partial string? Title { get; private set; }

    [ObservableProperty] public partial string? IdText { get; private set; }

    public bool HasCollection => Title is not null;

    public int ItemCount => Rows.Count;

    public bool IsCollectionEmpty => _collection is { Items.Count: 0 };

    public int MissingCount => Rows.Count(row => row.Item.State == WorkshopItemState.NotSubscribed);

    public int SubscribedCount => Rows.Count(row => row.Item.State != WorkshopItemState.NotSubscribed);

    public int OutdatedCount => Rows.Count(row => row.Item.State == WorkshopItemState.NeedsUpdate);

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenCommand), nameof(GenerateCommand))]
    public partial bool IsLoading { get; private set; }

    /// <summary>Steam actions may start: a collection is open, none runs, and no game from this launcher does.</summary>
    public override bool CanRunActions => HasCollection && base.CanRunActions;

    /// <summary>Re-reads the open collection's subscription states; for the section being shown.</summary>
    internal async Task ShowAsync()
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

    /// <summary>Opens the open collection again from the Web API.</summary>
    internal Task RefreshAsync() => _collection is { } collection ? LoadCollectionAsync(collection.Id) : Task.CompletedTask;

    internal override void RenderTexts()
    {
        base.RenderTexts();
        OnPropertyChanged(nameof(LinkErrorText));
    }

    private bool CanOpen() => !IsLoading && WorkshopLinks.TryParseItemId(LinkText, out _);

    [RelayCommand(CanExecute = nameof(CanOpen))]
    private Task OpenAsync() => WorkshopLinks.TryParseItemId(LinkText, out var id) ? LoadCollectionAsync(id) : Task.CompletedTask;

    [RelayCommand]
    private Task OpenRememberedAsync(RememberedWorkshopCollectionRow row) => LoadCollectionAsync(row.Id);

    [RelayCommand]
    private void Forget(RememberedWorkshopCollectionRow row)
    {
        _history.Forget(row.Id);
        RefreshRemembered();
    }

    private bool CanOpenCollectionInSteam() => HasCollection;

    [RelayCommand(CanExecute = nameof(CanOpenCollectionInSteam))]
    private Task OpenCollectionInSteamAsync() =>
        _collection is { } collection ? OpenInSteamAsync(collection.Id) : Task.CompletedTask;

    private bool CanSubscribeMissing() => CanRunActions && MissingCount > 0;

    [RelayCommand(CanExecute = nameof(CanSubscribeMissing))]
    private Task SubscribeMissingAsync() =>
        RunAsync(WorkshopAction.Subscribe, Rows.Where(row => row.Item.State == WorkshopItemState.NotSubscribed));

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

        var prompt = ModCollectionPrompts.NamePrompt(UiText.WorkshopCollections_GenerateTitle, collection.Title ?? "", UiText.ModCollections_Create);
        if (await Page.Collections.AskNameAsync(prompt, original: null) is not { } name || !await Page.Collections.SaveAndSelectAsync(name, ids))
        {
            return;
        }

        var count = ids.Count;
        ShownStatus.Show(() => string.Format(CultureInfo.CurrentCulture, UiText.WorkshopCollections_Generated, name, count));

        // The items' usage by launcher collections changed.
        await ShowAsync();
    }

    protected override void NotifyActionCommands()
    {
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
            RefreshRemembered();
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

    private async Task MergeAsync(int version)
    {
        if (_collection is not { } collection || Page.Install is not { } install)
        {
            return;
        }

        var result = await Service.GetSubscriptionsAsync(install.RootPath, Page.ExtraGameArgs(install));
        if (version != _loadVersion)
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
        RunActionAsync(action, WorkshopActionFlow.ActionItems(rows), () => Rows, ShowAsync);

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

    private void SetCollection(WorkshopCollection? collection)
    {
        _collection = collection;
        Title = collection is null ? null : collection.Title ?? collection.Id.ToString(CultureInfo.InvariantCulture);
        IdText = collection?.Id.ToString(CultureInfo.InvariantCulture);
        OnPropertyChanged(nameof(CanRunActions));
        OpenCollectionInSteamCommand.NotifyCanExecuteChanged();
        RefreshRemembered();
    }

    private void SetRows(IReadOnlyList<WorkshopItem> items)
    {
        Rows = [.. items.Select(item => new WorkshopItemRow(item, false, _ => { }))];
        NotifyActionCommands();
    }

    private void RefreshRemembered() =>
        Remembered =
        [
            .. _history.Items.Select(saved => new RememberedWorkshopCollectionRow(
                saved.Id, saved.Title ?? saved.Id.ToString(CultureInfo.InvariantCulture), saved.Id == _collection?.Id)),
        ];
}
