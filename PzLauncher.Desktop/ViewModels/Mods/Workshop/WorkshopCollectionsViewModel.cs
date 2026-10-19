using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PzLauncher.Core.Mod;
using PzLauncher.Core.Mod.Collections;
using PzLauncher.Core.Steam.Workshop;
using PzLauncher.Core.Steam.Workshop.Actions;
using PzLauncher.Desktop.Lang;
using PzLauncher.Desktop.Localization;
using PzLauncher.Desktop.Services.Mods;
using PzLauncher.Desktop.Services.Shell;

namespace PzLauncher.Desktop.ViewModels.Mods.Workshop;

/// <summary>A Steam Workshop collection opened by link or id, with each item's state for the account Steam signs in with and the install in use.</summary>
public partial class WorkshopCollectionsViewModel : ViewModelBase
{
    private readonly ModsViewModel _page;
    private readonly IWorkshopService _service;
    private readonly IWorkshopActionRunner _runner;
    private readonly IDialogService _dialogs;
    private readonly IUrlLauncher _urls;
    private readonly WorkshopCollectionHistory _history;

    private readonly LocalizedText _error;
    private readonly LocalizedText _status;
    private readonly LocalizedErrorList _warnings = new();
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
    {
        _page = page;
        _service = service;
        _runner = runner;
        _dialogs = dialogs;
        _urls = urls;
        _history = history;
        Rows = [];
        Remembered = [];
        _error = new LocalizedText(text => ErrorText = text);
        _status = new LocalizedText(text => StatusText = text);
        RefreshRemembered();
        page.PropertyChanged += OnPagePropertyChanged;
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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRunActions))]
    public partial bool IsActionRunning { get; private set; }

    [ObservableProperty] public partial string? ErrorText { get; private set; }

    [ObservableProperty] public partial string? StatusText { get; private set; }

    /// <summary>Files Steam's list could only be read in part from.</summary>
    public ObservableCollection<string> Warnings => _warnings.Items;

    /// <summary>Steam actions may start: a collection is open, none runs, and no game from this launcher does.</summary>
    public bool CanRunActions => HasCollection && _page.Install is not null && !_page.IsGameRunning && !IsActionRunning;

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

    internal void RenderTexts()
    {
        _error.Render();
        _status.Render();
        _warnings.Render();
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
        _collection is { } collection ? WorkshopActionFlow.OpenInSteamAsync(_urls, _dialogs, collection.Id) : Task.CompletedTask;

    [RelayCommand]
    private Task OpenInSteamAsync(WorkshopItemRow row) => WorkshopActionFlow.OpenInSteamAsync(_urls, _dialogs, row.Id);

    private bool CanSubscribeMissing() => CanRunActions && MissingCount > 0;

    [RelayCommand(CanExecute = nameof(CanSubscribeMissing))]
    private Task SubscribeMissingAsync() =>
        RunActionAsync(WorkshopAction.Subscribe, [.. Rows.Where(row => row.Item.State == WorkshopItemState.NotSubscribed)]);

    private bool CanUnsubscribeAll() => CanRunActions && SubscribedCount > 0;

    [RelayCommand(CanExecute = nameof(CanUnsubscribeAll))]
    private async Task UnsubscribeAllAsync()
    {
        List<WorkshopItemRow> subscribed = [.. Rows.Where(row => row.Item.State != WorkshopItemState.NotSubscribed)];
        if (subscribed.Count > 0 && await WorkshopActionFlow.ConfirmUnsubscribeAsync(_dialogs, subscribed))
        {
            await RunActionAsync(WorkshopAction.Unsubscribe, subscribed);
        }
    }

    private bool CanUpdateOutdated() => CanRunActions && OutdatedCount > 0;

    [RelayCommand(CanExecute = nameof(CanUpdateOutdated))]
    private Task UpdateOutdatedAsync() =>
        RunActionAsync(WorkshopAction.Update, [.. Rows.Where(row => row.Item.State == WorkshopItemState.NeedsUpdate)]);

    private bool CanGenerate() => HasCollection && Rows.Count > 0 && _page.CanWrite && !IsLoading && !IsActionRunning;

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
                skipped.Add(string.Format(CultureInfo.CurrentCulture, UiText.WorkshopCollections_SkippedFormat, WorkshopActionFlow.NameWithId(row), reason));
            }
            else
            {
                used.Add(row);
            }
        }

        if (used.Count == 0)
        {
            await _dialogs.ShowErrorAsync(UiText.WorkshopCollections_GenerateNothing);
            return;
        }

        List<InstalledMod> candidates = [.. used.SelectMany(row => row.Item.Mods)];
        List<WorkshopModPickItem> picks =
        [
            .. used
                .Where(row => row.Item.Mods.Count > 1)
                .Select(row => new WorkshopModPickItem(
                    row.Id,
                    WorkshopActionFlow.NameWithId(row),
                    [.. row.Item.Mods.Select(mod => new WorkshopModChoice(mod, Conflicts(mod, candidates)))])),
        ];
        if ((picks.Count > 0 || skipped.Count > 0) && !await _dialogs.PickWorkshopModsAsync(new WorkshopModPickViewModel(picks, skipped)))
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
            await _dialogs.ShowErrorAsync(UiText.WorkshopCollections_GenerateNothing);
            return;
        }

        var prompt = ModCollectionPrompts.NamePrompt(UiText.WorkshopCollections_GenerateTitle, collection.Title ?? "", UiText.ModCollections_Create);
        if (await ModCollectionPrompts.AskNameAsync(_dialogs, prompt, _page.Collections.Exists) is not TextPromptResult.Accepted accepted)
        {
            return;
        }

        var name = accepted.Text;
        if (!await _page.WriteAsync((root, extraGameArgs) => _page.Service.SaveAsync(new SaveModCollectionRequest(root, extraGameArgs, null, name, ids))))
        {
            return;
        }

        _page.Collections.SelectByName(name);
        var count = ids.Count;
        _status.Show(() => string.Format(CultureInfo.CurrentCulture, UiText.WorkshopCollections_Generated, name, count));

        // The items' usage by launcher collections changed.
        await ShowAsync();
    }

    partial void OnIsActionRunningChanged(bool value) => NotifyActionCommands();

    private async Task LoadCollectionAsync(ulong id)
    {
        var version = ++_loadVersion;
        IsLoading = true;
        _status.Clear();
        try
        {
            var result = await _service.GetCollectionAsync(id);
            if (version != _loadVersion)
            {
                return;
            }

            if (!result.IsSuccess)
            {
                SetCollection(null);
                SetRows([]);
                _error.Show(result.Error);
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
        if (_collection is not { } collection || _page.Install is not { } install)
        {
            return;
        }

        var result = await _service.GetSubscriptionsAsync(install.RootPath, _page.ExtraGameArgs(install));
        if (version != _loadVersion)
        {
            return;
        }

        if (!result.IsSuccess)
        {
            SetRows([]);
            _error.Show(result.Error);
            return;
        }

        _error.Clear();
        _warnings.Clear();
        foreach (var warning in result.Value.Warnings)
        {
            _warnings.Add(warning);
        }

        var subscribed = new Dictionary<ulong, WorkshopItem>();
        foreach (var item in result.Value.Items)
        {
            subscribed.TryAdd(item.Id, item);
        }

        SetRows([.. collection.Items.Select(entry => ItemOf(entry, subscribed))]);
    }

    private async Task RunActionAsync(WorkshopAction action, IReadOnlyList<WorkshopItemRow> rows)
    {
        if (!CanRunActions || rows.Count == 0 || _page.Install is not { } install)
        {
            return;
        }

        IsActionRunning = true;
        try
        {
            await WorkshopActionFlow.RunAsync(
                _runner, _service, _dialogs, install.RootPath, _page.ExtraGameArgs(install), action, WorkshopActionFlow.ActionItems(rows), () => Rows);
        }
        finally
        {
            IsActionRunning = false;
        }

        await ShowAsync();
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

    private void OnPagePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ModsViewModel.Install) or nameof(ModsViewModel.IsGameRunning):
                OnPropertyChanged(nameof(CanRunActions));
                NotifyActionCommands();
                break;
            case nameof(ModsViewModel.CanWrite):
                GenerateCommand.NotifyCanExecuteChanged();
                break;
        }
    }

    private void NotifyActionCommands()
    {
        SubscribeMissingCommand.NotifyCanExecuteChanged();
        UnsubscribeAllCommand.NotifyCanExecuteChanged();
        UpdateOutdatedCommand.NotifyCanExecuteChanged();
        GenerateCommand.NotifyCanExecuteChanged();
    }
}
