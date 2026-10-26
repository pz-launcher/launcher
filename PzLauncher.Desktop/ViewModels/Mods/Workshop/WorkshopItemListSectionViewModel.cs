using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using PzLauncher.Core.Common;
using PzLauncher.Core.Mod;
using PzLauncher.Core.Mod.Collections;
using PzLauncher.Core.Mod.Workshop;
using PzLauncher.Core.Steam.Workshop;
using PzLauncher.Core.Steam.Workshop.Actions;
using PzLauncher.Desktop.Lang;
using PzLauncher.Desktop.Localization;
using PzLauncher.Desktop.Services.Mods;
using PzLauncher.Desktop.Services.Mods.LocalCollections;
using PzLauncher.Desktop.Services.Shell.Dialogs;
using PzLauncher.Desktop.Services.Shell;
using PzLauncher.Desktop.ViewModels.Mods.Workshop.Actions;

namespace PzLauncher.Desktop.ViewModels.Mods.Workshop;

/// <summary>
/// A Workshop section listing items by id, an opened collection's or the starred ones, with each item's state for the account Steam signs in
/// with and the install in use, and the actions on the whole list. The collections it lists open in the Workshop collections section.
/// </summary>
public abstract partial class WorkshopItemListSectionViewModel : WorkshopSectionViewModel
{
    // In list order, with the Web API details known for them; null when nothing is listed.
    private IReadOnlyList<WorkshopCollectionItem>? _items;

    // The rows shown; one moved or removed changes it in place, so the list keeps the other rows' views.
    private ObservableCollection<WorkshopItemRow> _rows = [];

    private protected WorkshopItemListSectionViewModel(
        ModsViewModel page,
        IWorkshopService service,
        IWorkshopActionRunner runner,
        IDialogService dialogs,
        IUrlLauncher urls,
        WorkshopFavorites favorites,
        WorkshopTitles titles,
        LocalCollectionStore localCollections)
        : base(page, service, runner, dialogs, urls, favorites, titles, localCollections)
    {
        AddStateCommands(SubscribeMissingCommand, GenerateCommand);
    }

    public int ItemCount => Rows.Count;

    public int MissingCount => Rows.Count(row => row.Item.State == WorkshopItemState.NotSubscribed);

    public int SubscribedCount => Rows.Count(row => row.Item.State != WorkshopItemState.NotSubscribed);

    /// <summary>A list is shown and holds no item.</summary>
    public bool IsListEmpty => _items is { Count: 0 };

    /// <summary>The name a generated launcher mod collection starts with.</summary>
    protected abstract string GeneratedCollectionName { get; }

    private bool CanSubscribeMissing() => CanRunActions && MissingCount > 0;

    [RelayCommand(CanExecute = nameof(CanSubscribeMissing))]
    private Task SubscribeMissingAsync() =>
        RunAsync(WorkshopAction.Subscribe, Rows.Where(row => row.Item.State == WorkshopItemState.NotSubscribed));

    private bool CanGenerate() => Rows.Count > 0 && Page.CanWrite && !IsLoading && !IsActionRunning;

    /// <summary>A launcher mod collection of the installed items' mods, in list order.</summary>
    [RelayCommand(CanExecute = nameof(CanGenerate))]
    private async Task GenerateAsync()
    {
        List<WorkshopItemRow> used = [];
        List<string> skipped = [];
        foreach (var row in Rows)
        {
            if (SkipReason(row) is { } reason)
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

        List<InstalledMod> candidates = [.. used.SelectMany(row => row.Mods)];
        List<WorkshopModPickItem> picks =
        [
            .. used
                .Where(row => row.Mods.Count > 1)
                .Select(row => new WorkshopModPickItem(
                    row.Id,
                    WorkshopActionFlow.NameWithId(row.Item),
                    [.. row.Mods.Select(mod => new WorkshopModChoice(mod, ModLoadOrder.Conflicts(mod, candidates)))])),
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
                    : row.Mods.Select(mod => mod.Id))
                .Distinct(StringComparer.Ordinal),
        ];
        if (ids.Count == 0)
        {
            await Dialogs.ShowErrorAsync(UiText.WorkshopCollections_GenerateNothing);
            return;
        }

        if (await Page.CreateCollectionAsync(UiText.WorkshopCollections_GenerateTitle, GeneratedCollectionName, ids) is not { } name)
        {
            return;
        }

        var count = ids.Count;
        ShownStatus.Show(() => string.Format(CultureInfo.CurrentCulture, UiText.WorkshopCollections_Generated, name, count));

        // The items' usage by launcher collections changed.
        await ShowAgainAsync();
    }

    [RelayCommand]
    private Task OpenCollectionAsync(WorkshopCollectionRow row) => Page.OpenWorkshopCollectionAsync(row.Id);

    // A local write, never Steam's: available while a game or a Steam action runs.
    [RelayCommand]
    private void ToggleCollectionFavorite(WorkshopCollectionRow row) => Favorites.Toggle(WorkshopFavoriteKind.Collection, row.Id, row.Title);

    protected override void ClearRows() => SetRows([]);

    /// <param name="items"><c>null</c> lists nothing.</param>
    protected void SetItems(IReadOnlyList<WorkshopCollectionItem>? items)
    {
        _items = items;
        OnPropertyChanged(nameof(IsListEmpty));
    }

    /// <summary>Drops the listed items <paramref name="keep"/> rejects, rows included, without asking Steam or the Web API again.</summary>
    protected void KeepItems(Func<ulong, bool> keep)
    {
        if (_items is not { } items || items.All(entry => keep(entry.Id)))
        {
            return;
        }

        SetItems([.. items.Where(entry => keep(entry.Id))]);
        RearrangeRows([.. _rows.Where(row => keep(row.Id))]);
    }

    /// <summary>Lists the items in this order, rows included, without asking Steam or the Web API again; false when one isn't listed yet.</summary>
    protected bool ArrangeItems(IReadOnlyList<ulong> ids)
    {
        if (_items is not { } items)
        {
            return false;
        }

        var listed = items.ToDictionary(item => item.Id);
        if (!ids.All(listed.ContainsKey))
        {
            return false;
        }

        if (!items.Select(item => item.Id).SequenceEqual(ids))
        {
            var rows = _rows.ToDictionary(row => row.Id);
            SetItems([.. ids.Select(id => listed[id])]);
            RearrangeRows([.. ids.Where(rows.ContainsKey).Select(id => rows[id])]);
        }

        return true;
    }

    /// <summary>
    /// Reads the Web API's latest details of <paramref name="ids"/>, then lists what <paramref name="list"/> makes of them (<c>null</c>: nothing)
    /// merged with the subscriptions. Without the details the items are listed anyway; the error shows as a warning after <paramref name="warnings"/>.
    /// </summary>
    protected async Task ListLatestAsync(
        IReadOnlyList<ulong> ids,
        Func<IReadOnlyDictionary<ulong, WorkshopItemDetails>, IReadOnlyList<WorkshopCollectionItem>?> list,
        int version,
        CancellationToken ct,
        IReadOnlyList<Error>? warnings = null)
    {
        Dictionary<ulong, WorkshopItemDetails> latest = [];
        List<Error> shown = [.. warnings ?? []];
        if (ids.Count > 0)
        {
            var details = await Service.CheckUpdatesAsync(ids, ct);
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
                shown.Add(details.Error);
            }
        }

        if (list(latest) is not { } items)
        {
            SetItems(null);
            SetRows([]);
            return;
        }

        SetItems(items);
        await MergeAsync(version, ct);
        if (IsLatest(version))
        {
            foreach (var warning in shown)
            {
                AddWarning(warning);
            }
        }
    }

    /// <summary>Shows the listed items as of now with their state in the install's subscriptions.</summary>
    protected async Task MergeAsync(int version, CancellationToken ct)
    {
        if (_items is null || Page.Install is not { } install)
        {
            return;
        }

        var result = await Page.GetSubscriptionsAsync(install, ct);
        if (!IsLatest(version) || _items is not { } items)
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
        SetRows(WorkshopModItem.InListOrder(items, result.Value.Items));
    }

    protected void SetRows(IReadOnlyList<WorkshopModItem> items) =>
        ShowListRows([.. items.Select(item => new WorkshopItemRow(item, false, IsFavoriteItem(item.Item.Id), _ => { }))]);

    private void ShowListRows(IReadOnlyList<WorkshopItemRow> rows)
    {
        _rows = [.. rows];
        ShowRows(_rows);
        OnCountsChanged();
    }

    // Rows already shown, so their titles are known.
    private void RearrangeRows(IReadOnlyList<WorkshopItemRow> rows)
    {
        if (EditInPlace(rows))
        {
            OnRowsEdited();
        }
        else
        {
            _rows = [.. rows];
            Rows = _rows;
        }

        OnCountsChanged();
    }

    /// <summary>Turns the shown rows into <paramref name="rows"/> by removing or moving one; false, changing nothing, when that can't.</summary>
    private bool EditInPlace(IReadOnlyList<WorkshopItemRow> rows)
    {
        var first = 0;
        while (first < rows.Count && first < _rows.Count && _rows[first] == rows[first])
        {
            first++;
        }

        if (rows.Count == _rows.Count - 1)
        {
            List<WorkshopItemRow> rest = [.. _rows];
            rest.RemoveAt(first);
            if (!rest.SequenceEqual(rows))
            {
                return false;
            }

            _rows.RemoveAt(first);
            return true;
        }

        if (rows.Count != _rows.Count)
        {
            return false;
        }

        if (first == rows.Count)
        {
            return true;
        }

        var last = rows.Count - 1;
        while (_rows[last] == rows[last])
        {
            last--;
        }

        foreach (var (from, to) in new[] { (first, last), (last, first) })
        {
            List<WorkshopItemRow> moved = [.. _rows];
            moved.RemoveAt(from);
            moved.Insert(to, _rows[from]);
            if (moved.SequenceEqual(rows))
            {
                _rows.Move(from, to);
                return true;
            }
        }

        return false;
    }

    private void OnCountsChanged()
    {
        OnPropertyChanged(nameof(ItemCount));
        OnPropertyChanged(nameof(MissingCount));
        OnPropertyChanged(nameof(SubscribedCount));
    }

    private static string? SkipReason(WorkshopItemRow row) =>
        row.Item.State == WorkshopItemState.NotSubscribed ? DisplayNames.Of(WorkshopItemState.NotSubscribed)
        : row.Item.Installed is null ? DisplayNames.Of(WorkshopItemState.NotInstalled)
        : row.Mods.Count == 0 ? UiText.WorkshopCollections_SkippedNoMods
        : null;
}
