using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PzLauncher.Core.Common;
using PzLauncher.Core.Steam.Workshop;
using PzLauncher.Core.Steam.Workshop.Actions;
using PzLauncher.Desktop.Localization;
using PzLauncher.Desktop.Services.Mods;
using PzLauncher.Desktop.Services.Preferences;
using PzLauncher.Desktop.Services.Shell;

namespace PzLauncher.Desktop.ViewModels.Mods.Workshop;

/// <summary>What the Workshop sections of the Mods page share: texts, opening an item in Steam, starring it locally and running a Steam action.</summary>
public abstract partial class WorkshopSectionViewModel : ViewModelBase
{
    private readonly IUrlLauncher _urls;
    private readonly LocalizedErrorList _warnings = new();

    private protected WorkshopSectionViewModel(
        ModsViewModel page, IWorkshopService service, IWorkshopActionRunner runner, IDialogService dialogs, IUrlLauncher urls, WorkshopFavorites favorites)
    {
        Page = page;
        Service = service;
        Runner = runner;
        Dialogs = dialogs;
        Favorites = favorites;
        _urls = urls;
        ShownError = new LocalizedText(text => ErrorText = text);
        ShownStatus = new LocalizedText(text => StatusText = text);
        page.PropertyChanged += OnPagePropertyChanged;

        // Never unsubscribed: the sections and the favorites are app-lifetime singletons.
        favorites.Changed += (_, _) => OnFavoritesChanged();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRunActions))]
    public partial bool IsActionRunning { get; private set; }

    [ObservableProperty] public partial string? ErrorText { get; private set; }

    [ObservableProperty] public partial string? StatusText { get; private set; }

    /// <summary>Files Steam's list could only be read in part from, and for subscriptions a failed title lookup.</summary>
    public ObservableCollection<string> Warnings => _warnings.Items;

    /// <summary>Steam actions may start: none runs, and no game from this launcher does.</summary>
    public virtual bool CanRunActions => Page.Install is not null && !Page.IsGameRunning && !IsActionRunning;

    protected ModsViewModel Page { get; }

    protected IWorkshopService Service { get; }

    protected IWorkshopActionRunner Runner { get; }

    protected IDialogService Dialogs { get; }

    protected WorkshopFavorites Favorites { get; }

    protected LocalizedText ShownError { get; }

    protected LocalizedText ShownStatus { get; }

    internal virtual void RenderTexts()
    {
        ShownError.Render();
        ShownStatus.Render();
        _warnings.Render();
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

    /// <param name="rows">The rows shown when an event arrives; a reload may replace them during the run.</param>
    protected async Task RunActionAsync(
        WorkshopAction action, IReadOnlyList<WorkshopActionItemRow> items, Func<IReadOnlyList<WorkshopItemRow>> rows, Func<Task> reload)
    {
        if (!CanRunActions || items.Count == 0 || Page.Install is not { } install)
        {
            return;
        }

        IsActionRunning = true;
        try
        {
            await WorkshopActionFlow.RunAsync(Runner, Service, Dialogs, install.RootPath, Page.ExtraGameArgs(install), action, items, rows);
        }
        finally
        {
            IsActionRunning = false;
        }

        await reload();
    }

    /// <summary>Raises CanExecuteChanged for the commands that depend on <see cref="CanRunActions"/>.</summary>
    protected abstract void NotifyActionCommands();

    /// <summary>Any section, or an import, starred or unstarred something or a starred title changed.</summary>
    protected abstract void OnFavoritesChanged();

    protected bool IsFavoriteItem(ulong id) => Favorites.IsFavorite(WorkshopFavoriteKind.Item, id);

    protected void ShowFavorites(IEnumerable<WorkshopItemRow> rows)
    {
        foreach (var row in rows)
        {
            row.IsFavorite = IsFavoriteItem(row.Id);
        }
    }

    /// <summary>Keeps the Web API titles of starred items among the rows.</summary>
    protected void RememberTitles(IEnumerable<WorkshopItemRow> rows) =>
        Favorites.UpdateTitles(WorkshopFavoriteKind.Item, rows.Select(row => new SavedWorkshopEntry(row.Id, WorkshopItemRow.WebTitle(row.Item))));

    protected virtual void OnPageChanged(string? propertyName)
    {
        if (propertyName is nameof(ModsViewModel.Install) or nameof(ModsViewModel.IsGameRunning))
        {
            OnPropertyChanged(nameof(CanRunActions));
            NotifyActionCommands();
        }
    }

    protected Task OpenInSteamAsync(ulong id) => WorkshopActionFlow.OpenInSteamAsync(_urls, Dialogs, id);

    [RelayCommand]
    private Task OpenInSteamAsync(WorkshopItemRow row) => OpenInSteamAsync(row.Id);

    // A local write, never Steam's: available while a game or a Steam action runs.
    [RelayCommand]
    private void ToggleFavorite(WorkshopItemRow row) => Favorites.Toggle(WorkshopFavoriteKind.Item, row.Id, WorkshopItemRow.WebTitle(row.Item));

    partial void OnIsActionRunningChanged(bool value) => NotifyActionCommands();

    private void OnPagePropertyChanged(object? sender, PropertyChangedEventArgs e) => OnPageChanged(e.PropertyName);
}
