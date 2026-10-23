using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PzLauncher.Core.Common;
using PzLauncher.Core.Mod.Workshop;
using PzLauncher.Core.Steam.Workshop;
using PzLauncher.Core.Steam.Workshop.Actions;
using PzLauncher.Core.Steam.Workshop.Ids;
using PzLauncher.Desktop.Lang;
using PzLauncher.Desktop.Localization;
using PzLauncher.Desktop.Services.Mods;
using PzLauncher.Desktop.Services.Shell.Dialogs;
using PzLauncher.Desktop.Services.Shell;
using PzLauncher.Desktop.ViewModels.Mods.Workshop.Actions;

namespace PzLauncher.Desktop.ViewModels.Mods.Workshop;

/// <summary>The Steam Workshop subscriptions of the account Steam signs in with, in the Steam libraries of the install in use.</summary>
public partial class WorkshopSubscriptionsViewModel : WorkshopSectionViewModel
{
    private readonly IClipboardService _clipboard;
    private readonly RowSelection<ulong> _selection = new();
    private bool _loaded;

    internal WorkshopSubscriptionsViewModel(
        ModsViewModel page,
        IWorkshopService service,
        IWorkshopActionRunner runner,
        IDialogService dialogs,
        IClipboardService clipboard,
        IUrlLauncher urls,
        WorkshopFavorites favorites,
        WorkshopTitles titles)
        : base(page, service, runner, dialogs, urls, favorites, titles)
    {
        _clipboard = clipboard;
        VisibleRows = [];
        AddStateCommands(SubscribeCommand, UnsubscribeCommand, UpdateSelectedCommand, CheckUpdatesCommand);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFilterEmpty))]
    public partial IReadOnlyList<WorkshopItemRow> VisibleRows { get; private set; }

    [ObservableProperty] public partial string SearchText { get; set; } = "";

    [ObservableProperty] public partial WorkshopSubscriptionsFilter Filter { get; set; }

    [ObservableProperty] public partial WorkshopSubscriptionsSortColumn SortColumn { get; private set; }

    [ObservableProperty] public partial bool SortDescending { get; private set; }

    public int SelectedCount => _selection.Count;

    /// <summary>Loaded, and the account has no subscription.</summary>
    public bool IsEmpty => _loaded && Rows.Count == 0;

    public bool IsFilterEmpty => Rows.Count > 0 && VisibleRows.Count == 0;

    internal override Task ShowAsync() => LoadAsync();

    internal override Task RefreshAsync() => LoadAsync();

    [RelayCommand]
    private void ClearSelection()
    {
        foreach (var row in Rows)
        {
            row.IsSelected = false;
        }
    }

    [RelayCommand]
    private void SortBy(WorkshopSubscriptionsSortColumn column)
    {
        SortDescending = column == SortColumn && !SortDescending;
        SortColumn = column;
        Rows = Sorted(Rows);
        RefreshVisibleRows();
    }

    private bool CanCheckUpdates() => Page.Install is not null && !IsActionRunning;

    [RelayCommand(CanExecute = nameof(CanCheckUpdates))]
    private async Task CheckUpdatesAsync()
    {
        List<ulong> ids = [.. Rows.Where(row => row.Item.Installed is not null).Select(row => row.Id)];
        if (ids.Count > 0)
        {
            Error? error = null;
            await RunLoadAsync(async _ =>
            {
                var result = await Service.CheckUpdatesAsync(ids);
                if (result.IsSuccess)
                {
                    await LoadAsync();
                }
                else
                {
                    error = result.Error;
                }
            });
            if (error is not null)
            {
                await Dialogs.ShowErrorAsync(ErrorMessages.ToUserText(error));
                return;
            }
        }

        var outdated = OutdatedCount;
        ShownStatus.Show(() => outdated == 0
            ? UiText.Workshop_CheckedAllCurrent
            : string.Format(CultureInfo.CurrentCulture, UiText.Workshop_CheckedOutdated, outdated));
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
        if (await Dialogs.PromptTextAsync(prompt) is not TextPromptResult.Accepted accepted || !WorkshopLinks.TryParseItemId(accepted.Text, out var id))
        {
            return;
        }

        var name = Rows.FirstOrDefault(row => row.Id == id)?.Title ?? id.ToString(CultureInfo.InvariantCulture);
        await RunAsync(WorkshopAction.Subscribe, [new WorkshopActionItemRow(id, name)]);
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

        if (await WorkshopActionFlow.ConfirmUnsubscribeAsync(Dialogs, selected))
        {
            await RunAsync(WorkshopAction.Unsubscribe, selected);
        }
    }

    [RelayCommand(CanExecute = nameof(CanRunOnSelection))]
    private Task UpdateSelectedAsync() => RunAsync(WorkshopAction.Update, SelectedRows());

    partial void OnSearchTextChanged(string value) => RefreshVisibleRows();

    partial void OnFilterChanged(WorkshopSubscriptionsFilter value) => RefreshVisibleRows();

    protected override void OnFavoritesChanged()
    {
        ShowRowFavorites();
        if (Filter == WorkshopSubscriptionsFilter.Favorites)
        {
            RefreshVisibleRows();
        }
    }

    protected override Task ShowAgainAsync() => LoadAsync();

    protected override void ClearRows() => ShowSubscriptions(null);

    /// <summary>Reads Steam's local files, then fetches titles the Web API hasn't given this session.</summary>
    private Task LoadAsync()
    {
        if (Page.Install is not { } install)
        {
            Clear();
            return Task.CompletedTask;
        }

        var root = install.RootPath;
        var extraGameArgs = Page.ExtraGameArgs(install);
        return RunLoadAsync(async version =>
        {
            var result = await Page.WorkshopMods.GetSubscriptionsAsync(root, extraGameArgs);
            if (!IsLatest(version))
            {
                return;
            }

            if (!result.IsSuccess)
            {
                ShowSubscriptions(null);
                ShownError.Show(result.Error);
                return;
            }

            ShownError.Clear();
            ShowSubscriptions(result.Value);
            List<ulong> untitled = [.. result.Value.Items.Where(item => item.Item.Latest is null).Select(item => item.Item.Id)];
            if (untitled.Count == 0)
            {
                return;
            }

            var details = await Service.CheckUpdatesAsync(untitled);
            if (!IsLatest(version))
            {
                return;
            }

            if (!details.IsSuccess)
            {
                AddWarning(details.Error);
                return;
            }

            // Items the Web API doesn't show stay untitled; reading again would change nothing.
            if (details.Value.Count == 0)
            {
                return;
            }

            result = await Page.WorkshopMods.GetSubscriptionsAsync(root, extraGameArgs);
            if (!IsLatest(version))
            {
                return;
            }

            if (result.IsSuccess)
            {
                ShowSubscriptions(result.Value);
            }
            else
            {
                ShownError.Show(result.Error);
            }
        });
    }

    private List<WorkshopItemRow> SelectedRows() => [.. Rows.Where(row => row.IsSelected)];

    private void ShowSubscriptions(WorkshopModSubscriptions? subscriptions)
    {
        _loaded = subscriptions is not null;
        ShowWarnings(subscriptions?.Warnings ?? []);
        IReadOnlyList<WorkshopModItem> items = subscriptions?.Items ?? [];
        _selection.IntersectWith(items.Select(item => item.Item.Id));
        ShowRows(Sorted(items.Select(item =>
            new WorkshopItemRow(item, _selection.Contains(item.Item.Id), IsFavoriteItem(item.Item.Id), OnRowSelectionChanged))));
        RefreshVisibleRows();
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(SelectedCount));
    }

    private void OnRowSelectionChanged(WorkshopItemRow row)
    {
        _selection.Set(row.Id, row.IsSelected);
        OnPropertyChanged(nameof(SelectedCount));
        UnsubscribeCommand.NotifyCanExecuteChanged();
        UpdateSelectedCommand.NotifyCanExecuteChanged();
    }

    private void RefreshVisibleRows()
    {
        var search = SearchText.Trim();
        VisibleRows = [.. Rows.Where(row => MatchesFilter(row) && Matches(row, search))];
    }

    private bool MatchesFilter(WorkshopItemRow row) => Filter switch
    {
        WorkshopSubscriptionsFilter.All => true,
        WorkshopSubscriptionsFilter.NeedsUpdate => row.Item.State == WorkshopItemState.NeedsUpdate,
        WorkshopSubscriptionsFilter.Unused => row.Collections.Count == 0,
        WorkshopSubscriptionsFilter.Favorites => row.IsFavorite,
    };

    /// <summary>Title, item id or a mod id contains the search text, ignoring case; an empty search matches all.</summary>
    private static bool Matches(WorkshopItemRow row, string search) =>
        search.Length == 0
        || row.Title.Contains(search, StringComparison.CurrentCultureIgnoreCase)
        || row.IdText.Contains(search, StringComparison.Ordinal)
        || row.Mods.Any(mod => mod.Id.Contains(search, StringComparison.OrdinalIgnoreCase));

    private WorkshopItemRow[] Sorted(IEnumerable<WorkshopItemRow> rows) =>
        SortedRows.Sort(rows, SortColumn, SortDescending, IsBlank, CompareColumn, CompareByTitle);

    private static int CompareByTitle(WorkshopItemRow x, WorkshopItemRow y)
    {
        var order = StringComparer.CurrentCultureIgnoreCase.Compare(x.Title, y.Title);
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
