using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
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
using PzLauncher.Desktop.Services.Shell.Dialogs;
using PzLauncher.Desktop.Services.Shell;
using PzLauncher.Desktop.ViewModels.Mods.Workshop.Actions;

namespace PzLauncher.Desktop.ViewModels.Mods.Workshop;

/// <summary>
/// What the Workshop sections of the Mods page share: their loads, texts and item rows, running a Steam action on the rows, opening an item in
/// Steam, starring it locally and adding it to a local collection.
/// </summary>
public abstract partial class WorkshopSectionViewModel : ViewModelBase
{
    private readonly IUrlLauncher _urls;
    private readonly LocalizedErrorList _warnings = new();
    private readonly List<IRelayCommand> _stateCommands = [];

    // A newer load supersedes one still in flight.
    private int _loadVersion;

    private protected WorkshopSectionViewModel(
        ModsViewModel page,
        IWorkshopService service,
        IWorkshopActionRunner runner,
        IDialogService dialogs,
        IUrlLauncher urls,
        WorkshopFavorites favorites,
        WorkshopTitles titles,
        LocalCollections localCollections)
    {
        Page = page;
        Service = service;
        Runner = runner;
        Dialogs = dialogs;
        Favorites = favorites;
        Titles = titles;
        LocalCollections = localCollections;
        _urls = urls;
        Rows = [];
        ShownError = new LocalizedText(text => ErrorText = text);
        ShownStatus = new LocalizedText(text => StatusText = text);
        AddStateCommands(UpdateOutdatedCommand);
        page.PropertyChanged += OnPagePropertyChanged;

        // Never unsubscribed: the sections and the favorites are app-lifetime singletons.
        favorites.Changed += (_, _) => OnFavoritesChanged();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRunActions))]
    public partial bool IsActionRunning { get; private set; }

    [ObservableProperty] public partial bool IsLoading { get; private set; }

    /// <summary>Every row the section holds, before any filter.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OutdatedCount))]
    public partial IReadOnlyList<WorkshopItemRow> Rows { get; protected set; }

    public int OutdatedCount => Rows.Count(row => row.NeedsUpdate);

    [ObservableProperty] public partial string? ErrorText { get; private set; }

    [ObservableProperty] public partial string? StatusText { get; private set; }

    /// <summary>Files Steam's list could only be read in part from, and failed title lookups.</summary>
    public ObservableCollection<string> Warnings => _warnings.Items;

    /// <summary>Steam actions may start: none runs, and no game from this launcher does.</summary>
    public bool CanRunActions => Page.Install is not null && !Page.IsGameRunning && !IsActionRunning;

    protected ModsViewModel Page { get; }

    protected IWorkshopService Service { get; }

    protected IWorkshopActionRunner Runner { get; }

    protected IDialogService Dialogs { get; }

    protected WorkshopFavorites Favorites { get; }

    protected WorkshopTitles Titles { get; }

    protected LocalCollections LocalCollections { get; }

    protected LocalizedText ShownError { get; }

    protected LocalizedText ShownStatus { get; }

    /// <summary>For the section being shown: reads what may have changed since it was last shown.</summary>
    internal abstract Task ShowAsync();

    /// <summary>The page's refresh: reads everything the section shows again.</summary>
    internal abstract Task RefreshAsync();

    /// <summary>Drops what was read for the install in use, before another one is shown.</summary>
    internal void Clear()
    {
        ++_loadVersion;
        IsLoading = false;
        ShownError.Clear();
        ShownStatus.Clear();
        _warnings.Clear();
        ClearRows();
    }

    internal virtual void RenderTexts()
    {
        ShownError.Render();
        ShownStatus.Render();
        _warnings.Render();
    }

    /// <summary>Shows the rows again after something may have changed the items' states.</summary>
    protected abstract Task ShowAgainAsync();

    protected abstract void ClearRows();

    /// <summary>Any section, or an import, starred or unstarred something or a starred title changed.</summary>
    protected abstract void OnFavoritesChanged();

    /// <summary>Commands whose CanExecute reads the page, the rows, a load or a running action; each change re-evaluates them.</summary>
    protected void AddStateCommands(params IRelayCommand[] commands) => _stateCommands.AddRange(commands);

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

    /// <summary>Shows the rows, keeping the Web API titles of starred items among them.</summary>
    protected void ShowRows(IReadOnlyList<WorkshopItemRow> rows)
    {
        Rows = rows;
        Titles.SeenItems(rows.Select(row => new WorkshopEntry(row.Id, WorkshopItemRow.WebTitle(row.Item))));
    }

    protected void ShowWarnings(IEnumerable<Error> warnings)
    {
        _warnings.Clear();
        foreach (var warning in warnings)
        {
            _warnings.Add(warning);
        }
    }

    protected void AddWarning(Error warning) => _warnings.Add(warning);

    protected Task RunAsync(WorkshopAction action, IEnumerable<WorkshopItemRow> rows) => RunAsync(action, WorkshopActionFlow.ActionItems(rows));

    /// <summary>Shows the progress dialog for the run, then the rows again.</summary>
    protected async Task RunAsync(WorkshopAction action, IReadOnlyList<WorkshopActionItemRow> items)
    {
        if (!CanRunActions || items.Count == 0 || Page.Install is not { } install)
        {
            return;
        }

        IsActionRunning = true;
        try
        {
            // Rows read when each event arrives: a reload may replace them during the run.
            await WorkshopActionFlow.RunAsync(Runner, Service, Dialogs, install.RootPath, action, items, () => Rows);
        }
        finally
        {
            IsActionRunning = false;
        }

        await ShowAgainAsync();
    }

    protected bool IsFavoriteItem(ulong id) => Favorites.IsFavorite(WorkshopFavoriteKind.Item, id);

    protected bool IsFavoriteCollection(ulong id) => Favorites.IsFavorite(WorkshopFavoriteKind.Collection, id);

    protected void ShowRowFavorites()
    {
        foreach (var row in Rows)
        {
            row.IsFavorite = IsFavoriteItem(row.Id);
        }
    }

    protected Task OpenInSteamAsync(ulong id) => WorkshopActionFlow.OpenInSteamAsync(_urls, Dialogs, id);

    protected static WorkshopEntry EntryOf(WorkshopItemRow row) => new(row.Id, WorkshopItemRow.WebTitle(row.Item));

    /// <summary>Asks for a local collection, existing or new, and adds the items not in it yet at its end.</summary>
    protected async Task AddEntriesToLocalCollectionAsync(IReadOnlyList<WorkshopEntry> items)
    {
        if (items.Count == 0)
        {
            return;
        }

        var pick = new LocalCollectionPickViewModel(LocalCollections);
        if (!await Dialogs.PickLocalCollectionAsync(pick))
        {
            return;
        }

        string name;
        int added;
        switch (pick.Target)
        {
            case LocalCollectionTarget.Existing existing when LocalCollections.Find(existing.Id) is { } collection:
                name = collection.Name;
                added = LocalCollections.Add(collection.Id, items);
                break;
            case LocalCollectionTarget.New created:
                var made = LocalCollections.Create(created.Name, items);
                name = made.Name;
                added = made.Items.Count;
                break;
            default:
                return;
        }

        var skipped = items.DistinctBy(item => item.Id).Count() - added;
        ShownStatus.Show(() => string.Format(CultureInfo.CurrentCulture, UiText.LocalCollections_AddedTo, added, name, skipped));
    }

    private bool CanUpdateOutdated() => CanRunActions && OutdatedCount > 0;

    [RelayCommand(CanExecute = nameof(CanUpdateOutdated))]
    private Task UpdateOutdatedAsync() => RunAsync(WorkshopAction.Update, Rows.Where(row => row.NeedsUpdate));

    [RelayCommand]
    private Task OpenInSteamAsync(WorkshopItemRow row) => OpenInSteamAsync(row.Id);

    // A local write, never Steam's: available while a game or a Steam action runs.
    [RelayCommand]
    private void ToggleFavorite(WorkshopItemRow row) => Favorites.Toggle(WorkshopFavoriteKind.Item, row.Id, WorkshopItemRow.WebTitle(row.Item));

    // A local write, never Steam's: available while a game or a Steam action runs.
    [RelayCommand]
    private Task AddToLocalCollectionAsync(WorkshopItemRow row) => AddEntriesToLocalCollectionAsync([EntryOf(row)]);

    partial void OnIsActionRunningChanged(bool value) => NotifyStateCommands();

    partial void OnIsLoadingChanged(bool value) => NotifyStateCommands();

    partial void OnRowsChanged(IReadOnlyList<WorkshopItemRow> value) => NotifyStateCommands();

    private void NotifyStateCommands()
    {
        foreach (var command in _stateCommands)
        {
            command.NotifyCanExecuteChanged();
        }
    }

    private void OnPagePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ModsViewModel.Install) or nameof(ModsViewModel.IsGameRunning))
        {
            OnPropertyChanged(nameof(CanRunActions));
        }

        if (e.PropertyName is nameof(ModsViewModel.Install) or nameof(ModsViewModel.IsGameRunning) or nameof(ModsViewModel.CanWrite))
        {
            NotifyStateCommands();
        }
    }
}
