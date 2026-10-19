using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PzLauncher.Core.Steam.Workshop;
using PzLauncher.Core.Steam.Workshop.Actions;
using PzLauncher.Desktop.Lang;
using PzLauncher.Desktop.Localization;
using PzLauncher.Desktop.Services.Shell;

namespace PzLauncher.Desktop.ViewModels.Mods.Workshop;

/// <summary>The Steam Workshop subscriptions of the account Steam signs in with, in the Steam libraries of the install in use.</summary>
public partial class WorkshopSubscriptionsViewModel : ViewModelBase
{
    private readonly ModsViewModel _page;
    private readonly IWorkshopService _service;
    private readonly IWorkshopActionRunner _runner;
    private readonly IDialogService _dialogs;
    private readonly IClipboardService _clipboard;
    private readonly IUrlLauncher _urls;

    // By id, so a reload keeps what was ticked.
    private readonly HashSet<ulong> _selectedIds = [];
    private readonly LocalizedText _error;
    private readonly LocalizedText _status;
    private readonly LocalizedErrorList _warnings = new();
    private IReadOnlyList<WorkshopItemRow> _rows = [];
    private bool _loaded;

    // A newer load supersedes one still in flight.
    private int _loadVersion;

    internal WorkshopSubscriptionsViewModel(
        ModsViewModel page,
        IWorkshopService service,
        IWorkshopActionRunner runner,
        IDialogService dialogs,
        IClipboardService clipboard,
        IUrlLauncher urls)
    {
        _page = page;
        _service = service;
        _runner = runner;
        _dialogs = dialogs;
        _clipboard = clipboard;
        _urls = urls;
        VisibleRows = [];
        _error = new LocalizedText(text => ErrorText = text);
        _status = new LocalizedText(text => StatusText = text);
        page.PropertyChanged += OnPagePropertyChanged;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFilterEmpty))]
    public partial IReadOnlyList<WorkshopItemRow> VisibleRows { get; private set; }

    [ObservableProperty] public partial string SearchText { get; set; } = "";

    [ObservableProperty] public partial WorkshopSubscriptionsFilter Filter { get; set; }

    [ObservableProperty] public partial WorkshopSubscriptionsSortColumn SortColumn { get; private set; }

    [ObservableProperty] public partial bool SortDescending { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsProgress))]
    public partial bool IsLoading { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsProgress))]
    [NotifyCanExecuteChangedFor(nameof(CheckUpdatesCommand))]
    public partial bool IsChecking { get; private set; }

    public bool ShowsProgress => IsLoading || IsChecking;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRunActions))]
    [NotifyCanExecuteChangedFor(nameof(CheckUpdatesCommand))]
    public partial bool IsActionRunning { get; private set; }

    [ObservableProperty] public partial string? ErrorText { get; private set; }

    [ObservableProperty] public partial string? StatusText { get; private set; }

    /// <summary>Files Steam's list could only be read in part from, and a failed title lookup.</summary>
    public ObservableCollection<string> Warnings => _warnings.Items;

    public int SelectedCount => _selectedIds.Count;

    public int OutdatedCount => _rows.Count(row => row.Item.State == WorkshopItemState.NeedsUpdate);

    /// <summary>Steam actions may start: none runs, and no game from this launcher does.</summary>
    public bool CanRunActions => _page.Install is not null && !_page.IsGameRunning && !IsActionRunning;

    /// <summary>Loaded, and the account has no subscription.</summary>
    public bool IsEmpty => _loaded && _rows.Count == 0;

    public bool IsFilterEmpty => _rows.Count > 0 && VisibleRows.Count == 0;

    /// <summary>Reads Steam's local files, then fetches titles the Web API hasn't given this session.</summary>
    internal async Task LoadAsync()
    {
        var version = ++_loadVersion;
        if (_page.Install is not { } install)
        {
            Clear();
            return;
        }

        var root = install.RootPath;
        var extraGameArgs = _page.ExtraGameArgs(install);
        IsLoading = true;
        try
        {
            var result = await _service.GetSubscriptionsAsync(root, extraGameArgs);
            if (version != _loadVersion)
            {
                return;
            }

            if (!result.IsSuccess)
            {
                Show(null);
                _error.Show(result.Error);
                return;
            }

            _error.Clear();
            Show(result.Value);
            List<ulong> untitled = [.. result.Value.Items.Where(item => item.Latest is null).Select(item => item.Id)];
            if (untitled.Count == 0)
            {
                return;
            }

            var details = await _service.CheckUpdatesAsync(untitled);
            if (version != _loadVersion)
            {
                return;
            }

            if (!details.IsSuccess)
            {
                _warnings.Add(details.Error);
                return;
            }

            // Items the Web API doesn't show stay untitled; reading again would change nothing.
            if (details.Value.Count == 0)
            {
                return;
            }

            result = await _service.GetSubscriptionsAsync(root, extraGameArgs);
            if (version != _loadVersion)
            {
                return;
            }

            if (result.IsSuccess)
            {
                Show(result.Value);
            }
            else
            {
                _error.Show(result.Error);
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

    internal void Clear()
    {
        ++_loadVersion;
        IsLoading = false;
        _error.Clear();
        _status.Clear();
        Show(null);
    }

    internal void RenderTexts()
    {
        _error.Render();
        _status.Render();
        _warnings.Render();
    }

    [RelayCommand]
    private void ClearSelection()
    {
        foreach (var row in _rows)
        {
            row.IsSelected = false;
        }
    }

    [RelayCommand]
    private void SortBy(WorkshopSubscriptionsSortColumn column)
    {
        SortDescending = column == SortColumn && !SortDescending;
        SortColumn = column;
        _rows = Sorted(_rows);
        RefreshVisibleRows();
    }

    [RelayCommand]
    private Task OpenInSteamAsync(WorkshopItemRow row) => WorkshopActionFlow.OpenInSteamAsync(_urls, _dialogs, row.Id);

    private bool CanCheckUpdates() => _page.Install is not null && !IsChecking && !IsActionRunning;

    [RelayCommand(CanExecute = nameof(CanCheckUpdates))]
    private async Task CheckUpdatesAsync()
    {
        List<ulong> ids = [.. _rows.Where(row => row.Item.Installed is not null).Select(row => row.Id)];
        IsChecking = true;
        try
        {
            if (ids.Count > 0)
            {
                var result = await _service.CheckUpdatesAsync(ids);
                if (!result.IsSuccess)
                {
                    await _dialogs.ShowErrorAsync(ErrorMessages.ToUserText(result.Error));
                    return;
                }

                await LoadAsync();
            }

            var outdated = OutdatedCount;
            _status.Show(() => outdated == 0
                ? UiText.Workshop_CheckedAllCurrent
                : string.Format(CultureInfo.CurrentCulture, UiText.Workshop_CheckedOutdated, outdated));
        }
        finally
        {
            IsChecking = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRunActions))]
    private async Task SubscribeAsync()
    {
        // Offered only when it holds an item, so unrelated clipboard text never shows up in the box.
        var clipboard = await _clipboard.GetTextAsync();
        var initial = clipboard is not null && WorkshopLinks.TryParseItemId(clipboard, out _) ? clipboard.Trim() : "";
        var prompt = new TextPrompt(
            UiText.Workshop_SubscribeTitle,
            UiText.Workshop_SubscribeMessage,
            initial,
            UiText.Workshop_SubscribeAccept,
            text => WorkshopLinks.TryParseItemId(text, out _) ? null : UiText.Workshop_LinkInvalid);
        if (await _dialogs.PromptTextAsync(prompt) is not TextPromptResult.Accepted accepted || !WorkshopLinks.TryParseItemId(accepted.Text, out var id))
        {
            return;
        }

        var name = _rows.FirstOrDefault(row => row.Id == id)?.Title ?? id.ToString(CultureInfo.InvariantCulture);
        await RunActionAsync(WorkshopAction.Subscribe, [new WorkshopActionItemRow(id, name)]);
    }

    private bool CanRunOnSelection() => CanRunActions && SelectedCount > 0;

    [RelayCommand(CanExecute = nameof(CanRunOnSelection))]
    private async Task UnsubscribeAsync()
    {
        var selected = SelectedRows();
        if (selected.Count == 0)
        {
            return;
        }

        if (await WorkshopActionFlow.ConfirmUnsubscribeAsync(_dialogs, selected))
        {
            await RunActionAsync(WorkshopAction.Unsubscribe, WorkshopActionFlow.ActionItems(selected));
        }
    }

    private bool CanUpdateAll() => CanRunActions && OutdatedCount > 0;

    [RelayCommand(CanExecute = nameof(CanUpdateAll))]
    private Task UpdateAllAsync() =>
        RunActionAsync(WorkshopAction.Update, WorkshopActionFlow.ActionItems(_rows.Where(row => row.Item.State == WorkshopItemState.NeedsUpdate)));

    [RelayCommand(CanExecute = nameof(CanRunOnSelection))]
    private Task UpdateSelectedAsync() => RunActionAsync(WorkshopAction.Update, WorkshopActionFlow.ActionItems(SelectedRows()));

    partial void OnSearchTextChanged(string value) => RefreshVisibleRows();

    partial void OnFilterChanged(WorkshopSubscriptionsFilter value) => RefreshVisibleRows();

    partial void OnIsActionRunningChanged(bool value) => NotifyActionCommands();

    private async Task RunActionAsync(WorkshopAction action, IReadOnlyList<WorkshopActionItemRow> items)
    {
        if (!CanRunActions || items.Count == 0 || _page.Install is not { } install)
        {
            return;
        }

        IsActionRunning = true;
        try
        {
            await WorkshopActionFlow.RunAsync(_runner, _service, _dialogs, install.RootPath, _page.ExtraGameArgs(install), action, items, () => _rows);
        }
        finally
        {
            IsActionRunning = false;
        }

        await LoadAsync();
    }

    private List<WorkshopItemRow> SelectedRows() => [.. _rows.Where(row => row.IsSelected)];

    private void Show(WorkshopSubscriptions? subscriptions)
    {
        _loaded = subscriptions is not null;
        _warnings.Clear();
        foreach (var warning in subscriptions?.Warnings ?? [])
        {
            _warnings.Add(warning);
        }

        IReadOnlyList<WorkshopItem> items = subscriptions?.Items ?? [];
        _selectedIds.IntersectWith(items.Select(item => item.Id));
        _rows = Sorted([.. items.Select(item => new WorkshopItemRow(item, _selectedIds.Contains(item.Id), OnRowSelectionChanged))]);
        RefreshVisibleRows();
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(OutdatedCount));
        NotifyActionCommands();
    }

    private void OnRowSelectionChanged(WorkshopItemRow row)
    {
        if (row.IsSelected)
        {
            _selectedIds.Add(row.Id);
        }
        else
        {
            _selectedIds.Remove(row.Id);
        }

        OnPropertyChanged(nameof(SelectedCount));
        UnsubscribeCommand.NotifyCanExecuteChanged();
        UpdateSelectedCommand.NotifyCanExecuteChanged();
    }

    private void OnPagePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ModsViewModel.Install) or nameof(ModsViewModel.IsGameRunning))
        {
            OnPropertyChanged(nameof(CanRunActions));
            NotifyActionCommands();
            CheckUpdatesCommand.NotifyCanExecuteChanged();
        }
    }

    private void NotifyActionCommands()
    {
        SubscribeCommand.NotifyCanExecuteChanged();
        UnsubscribeCommand.NotifyCanExecuteChanged();
        UpdateAllCommand.NotifyCanExecuteChanged();
        UpdateSelectedCommand.NotifyCanExecuteChanged();
    }

    private void RefreshVisibleRows()
    {
        var search = SearchText.Trim();
        VisibleRows = [.. _rows.Where(row => MatchesFilter(row) && Matches(row, search))];
    }

    private bool MatchesFilter(WorkshopItemRow row) => Filter switch
    {
        WorkshopSubscriptionsFilter.All => true,
        WorkshopSubscriptionsFilter.NeedsUpdate => row.Item.State == WorkshopItemState.NeedsUpdate,
        WorkshopSubscriptionsFilter.Unused => row.Item.Collections.Count == 0,
    };

    /// <summary>Title, item id or a mod id contains the search text, ignoring case; an empty search matches all.</summary>
    private static bool Matches(WorkshopItemRow row, string search) =>
        search.Length == 0
        || row.Title.Contains(search, StringComparison.CurrentCultureIgnoreCase)
        || row.IdText.Contains(search, StringComparison.Ordinal)
        || row.Item.Mods.Any(mod => mod.Id.Contains(search, StringComparison.OrdinalIgnoreCase));

    private WorkshopItemRow[] Sorted(IReadOnlyList<WorkshopItemRow> rows)
    {
        var column = SortColumn;
        var sign = SortDescending ? -1 : 1;
        return [.. rows.Order(Comparer<WorkshopItemRow>.Create((x, y) => CompareRows(x, y, column, sign)))];
    }

    private static int CompareRows(WorkshopItemRow x, WorkshopItemRow y, WorkshopSubscriptionsSortColumn column, int sign)
    {
        // Blank cells stay at the bottom in both directions.
        var xBlank = IsBlank(x, column);
        var yBlank = IsBlank(y, column);
        if (xBlank != yBlank)
        {
            return xBlank ? 1 : -1;
        }

        var order = xBlank ? 0 : sign * CompareColumn(x, y, column);
        if (order != 0)
        {
            return order;
        }

        order = StringComparer.CurrentCultureIgnoreCase.Compare(x.Title, y.Title);
        return order != 0 ? order : x.Id.CompareTo(y.Id);
    }

    private static bool IsBlank(WorkshopItemRow row, WorkshopSubscriptionsSortColumn column) => column switch
    {
        WorkshopSubscriptionsSortColumn.Title => !row.HasTitle,
        WorkshopSubscriptionsSortColumn.Id => false,
        WorkshopSubscriptionsSortColumn.Mods => row.ModIds.Length == 0,
        WorkshopSubscriptionsSortColumn.Size => row.SizeMb is null,
        WorkshopSubscriptionsSortColumn.Updated => row.Updated is null,
        WorkshopSubscriptionsSortColumn.State => false,
        WorkshopSubscriptionsSortColumn.Collections => row.CollectionNames.Length == 0,
    };

    private static int CompareColumn(WorkshopItemRow x, WorkshopItemRow y, WorkshopSubscriptionsSortColumn column) => column switch
    {
        WorkshopSubscriptionsSortColumn.Title => StringComparer.CurrentCultureIgnoreCase.Compare(x.Title, y.Title),
        WorkshopSubscriptionsSortColumn.Id => x.Id.CompareTo(y.Id),
        WorkshopSubscriptionsSortColumn.Mods => StringComparer.OrdinalIgnoreCase.Compare(x.ModIds, y.ModIds),
        WorkshopSubscriptionsSortColumn.Size => Nullable.Compare(x.SizeMb, y.SizeMb),
        WorkshopSubscriptionsSortColumn.Updated => Nullable.Compare(y.Updated, x.Updated),
        WorkshopSubscriptionsSortColumn.State => StateRank(x.Item.State).CompareTo(StateRank(y.Item.State)),
        WorkshopSubscriptionsSortColumn.Collections => StringComparer.CurrentCultureIgnoreCase.Compare(x.CollectionNames, y.CollectionNames),
    };

    // Items that need attention first.
    private static int StateRank(WorkshopItemState state) => state switch
    {
        WorkshopItemState.NeedsUpdate => 0,
        WorkshopItemState.NotInstalled => 1,
        WorkshopItemState.DisabledLocally => 2,
        WorkshopItemState.UpToDate => 3,
        WorkshopItemState.NotSubscribed => 4,
    };
}
