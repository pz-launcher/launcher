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

/// <summary>
/// A Workshop section listing items by id, an opened collection's or the starred ones, with each item's state for the account Steam signs in
/// with and the install in use, and the actions on the whole list.
/// </summary>
public abstract partial class WorkshopItemListSectionViewModel : WorkshopSectionViewModel
{
    // In list order, with the Web API details known for them; null when nothing is listed.
    private IReadOnlyList<WorkshopCollectionItem>? _items;

    // A newer load supersedes one still in flight.
    private int _loadVersion;

    private protected WorkshopItemListSectionViewModel(
        ModsViewModel page, IWorkshopService service, IWorkshopActionRunner runner, IDialogService dialogs, IUrlLauncher urls, WorkshopFavorites favorites)
        : base(page, service, runner, dialogs, urls, favorites)
    {
        Rows = [];
    }

    /// <summary>The listed items in list order.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ItemCount), nameof(MissingCount), nameof(SubscribedCount), nameof(OutdatedCount))]
    public partial IReadOnlyList<WorkshopItemRow> Rows { get; private set; }

    public int ItemCount => Rows.Count;

    public int MissingCount => Rows.Count(row => row.Item.State == WorkshopItemState.NotSubscribed);

    public int SubscribedCount => Rows.Count(row => row.Item.State != WorkshopItemState.NotSubscribed);

    public int OutdatedCount => Rows.Count(row => row.Item.State == WorkshopItemState.NeedsUpdate);

    /// <summary>A list is shown and holds no item.</summary>
    public bool IsListEmpty => _items is { Count: 0 };

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GenerateCommand))]
    public partial bool IsLoading { get; private set; }

    /// <summary>Steam actions may start: a list is shown, none runs, and no game from this launcher does.</summary>
    public override bool CanRunActions => _items is not null && base.CanRunActions;

    /// <summary>The name a generated launcher mod collection starts with.</summary>
    protected abstract string GeneratedCollectionName { get; }

    /// <summary>Shows the list again after something may have changed the items' states.</summary>
    protected abstract Task ShowAgainAsync();

    private bool CanSubscribeMissing() => CanRunActions && MissingCount > 0;

    [RelayCommand(CanExecute = nameof(CanSubscribeMissing))]
    private Task SubscribeMissingAsync() =>
        RunAsync(WorkshopAction.Subscribe, Rows.Where(row => row.Item.State == WorkshopItemState.NotSubscribed));

    private bool CanUpdateOutdated() => CanRunActions && OutdatedCount > 0;

    [RelayCommand(CanExecute = nameof(CanUpdateOutdated))]
    private Task UpdateOutdatedAsync() =>
        RunAsync(WorkshopAction.Update, Rows.Where(row => row.Item.State == WorkshopItemState.NeedsUpdate));

    private bool CanGenerate() => Rows.Count > 0 && Page.CanWrite && !IsLoading && !IsActionRunning;

    /// <summary>A launcher mod collection of the installed items' mods, in list order.</summary>
    [RelayCommand(CanExecute = nameof(CanGenerate))]
    private async Task GenerateAsync()
    {
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

        var prompt = ModCollectionPrompts.NamePrompt(UiText.WorkshopCollections_GenerateTitle, GeneratedCollectionName, UiText.ModCollections_Create);
        if (await Page.Collections.AskNameAsync(prompt, original: null) is not { } name || !await Page.Collections.SaveAndSelectAsync(name, ids))
        {
            return;
        }

        var count = ids.Count;
        ShownStatus.Show(() => string.Format(CultureInfo.CurrentCulture, UiText.WorkshopCollections_Generated, name, count));

        // The items' usage by launcher collections changed.
        await ShowAgainAsync();
    }

    protected override void NotifyActionCommands()
    {
        SubscribeMissingCommand.NotifyCanExecuteChanged();
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

    protected virtual void OnLoadingChanged()
    {
    }

    /// <summary>Runs one load; <see cref="IsLatest"/> tells it whether a newer one started since.</summary>
    protected async Task RunLoadAsync(Func<int, Task> load)
    {
        var version = ++_loadVersion;
        IsLoading = true;
        try
        {
            await load(version);
        }
        finally
        {
            if (version == _loadVersion)
            {
                IsLoading = false;
            }
        }
    }

    protected bool IsLatest(int version) => version == _loadVersion;

    /// <param name="items"><c>null</c> lists nothing.</param>
    protected void SetItems(IReadOnlyList<WorkshopCollectionItem>? items)
    {
        _items = items;
        OnPropertyChanged(nameof(IsListEmpty));
        OnPropertyChanged(nameof(CanRunActions));
    }

    /// <summary>Drops the listed items <paramref name="keep"/> rejects, rows included, without asking Steam or the Web API again.</summary>
    protected void KeepItems(Func<ulong, bool> keep)
    {
        if (_items is not { } items || items.All(entry => keep(entry.Id)))
        {
            return;
        }

        _items = [.. items.Where(entry => keep(entry.Id))];
        Rows = [.. Rows.Where(row => keep(row.Id))];
        OnPropertyChanged(nameof(IsListEmpty));
        NotifyActionCommands();
    }

    /// <summary>Shows the listed items as of now with their state in the install's subscriptions.</summary>
    protected async Task MergeAsync(int version)
    {
        if (_items is null || Page.Install is not { } install)
        {
            return;
        }

        var result = await Service.GetSubscriptionsAsync(install.RootPath, Page.ExtraGameArgs(install));
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
        var subscribed = new Dictionary<ulong, WorkshopItem>();
        foreach (var item in result.Value.Items)
        {
            subscribed.TryAdd(item.Id, item);
        }

        SetRows([.. items.Select(entry => ItemOf(entry, subscribed))]);
    }

    protected void SetRows(IReadOnlyList<WorkshopItem> items)
    {
        Rows = [.. items.Select(item => new WorkshopItemRow(item, false, IsFavoriteItem(item.Id), _ => { }))];
        NotifyActionCommands();
        RememberTitles(Rows);
    }

    protected Task RunAsync(WorkshopAction action, IEnumerable<WorkshopItemRow> rows) =>
        RunActionAsync(action, WorkshopActionFlow.ActionItems(rows), () => Rows, ShowAgainAsync);

    partial void OnIsLoadingChanged(bool value) => OnLoadingChanged();

    // The list's own details fill in a title the subscription list hasn't fetched.
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
}
