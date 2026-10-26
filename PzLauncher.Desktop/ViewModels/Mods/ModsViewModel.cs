using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PzLauncher.Core.Common;
using PzLauncher.Core.Installs;
using PzLauncher.Core.Mod.Collections;
using PzLauncher.Core.Mod.Workshop;
using PzLauncher.Core.Steam.Workshop;
using PzLauncher.Core.Steam.Workshop.Actions;
using PzLauncher.Desktop.Localization;
using PzLauncher.Desktop.Services.Launch;
using PzLauncher.Desktop.Services.Mods;
using PzLauncher.Desktop.Services.Mods.LocalCollections;
using PzLauncher.Desktop.Services.Mods.Modpacks;
using PzLauncher.Desktop.Services.Shell.Dialogs;
using PzLauncher.Desktop.Services.Shell;
using PzLauncher.Desktop.ViewModels.Installs;
using PzLauncher.Desktop.ViewModels.Mods.Modpacks;
using PzLauncher.Desktop.ViewModels.Mods.Collections;
using PzLauncher.Desktop.ViewModels.Mods.Installed;
using PzLauncher.Desktop.ViewModels.Mods.Workshop;
using PzLauncher.Desktop.ViewModels.Mods.Workshop.LocalCollections;

namespace PzLauncher.Desktop.ViewModels.Mods;

/// <summary>The mods page of the game install in use: the game-file sections share one loaded state that every write re-reads; the Workshop sections load their own.</summary>
public partial class ModsViewModel : ViewModelBase
{
    private readonly InstallsViewModel _installs;
    private readonly IInstallOverridesService _overrides;
    private readonly IDialogService _dialogs;
    private readonly GameRunState _runState;
    private readonly IModCollectionOrderWriter _orderWriter;
    private readonly IModCollectionDefaultsService _defaults;
    private readonly ModCollectionApplier _applier;
    private readonly LocalCollectionStore _localCollections;
    private readonly ModpackStore _modpacks;
    private readonly PresetWriter _presets;

    private readonly LocalizedText _error;
    private bool _activated;

    // A newer load supersedes one still in flight.
    private int _loadVersion;

    // The shown install's subscriptions, read or being read, which the list sections share until a show, refresh, Steam action or install change.
    private SharedSubscriptions? _subscriptions;

    public ModsViewModel(
        InstallsViewModel installs,
        IModCollectionService service,
        IInstallOverridesService overrides,
        IModCollectionDefaultsService defaults,
        ModCollectionApplier applier,
        IDialogService dialogs,
        IClipboardService clipboard,
        GameRunState runState,
        IModCollectionOrderWriter orderWriter,
        IWorkshopService workshop,
        IWorkshopModsService workshopMods,
        IWorkshopActionRunner workshopActions,
        IUrlLauncher urls,
        WorkshopCollectionHistory workshopHistory,
        WorkshopUserCollectionsCache workshopUserCollections,
        WorkshopFavorites workshopFavorites,
        WorkshopTitles workshopTitles,
        LocalCollectionStore localCollections,
        ModpackStore modpacks,
        ILocalizationService localization)
    {
        _installs = installs;
        Service = service;
        WorkshopMods = workshopMods;
        _overrides = overrides;
        _dialogs = dialogs;
        _runState = runState;
        _orderWriter = orderWriter;
        _defaults = defaults;
        _applier = applier;
        _localCollections = localCollections;
        _modpacks = modpacks;
        _presets = new PresetWriter(service, defaults, modpacks);
        Mods = new InstalledModsViewModel(this);
        Collections = new ModCollectionsViewModel(this, defaults, dialogs, clipboard);
        Modpacks = new ModpacksViewModel(
            this, workshop, workshopActions, dialogs, clipboard, urls, workshopFavorites, workshopTitles, localCollections, modpacks, workshopHistory,
            workshopUserCollections, new ModpackImport(_presets, localCollections, modpacks, workshopHistory));
        WorkshopSubscriptions = new WorkshopSubscriptionsViewModel(
            this, workshop, workshopActions, dialogs, clipboard, urls, workshopFavorites, workshopTitles, localCollections);
        WorkshopCollections = new WorkshopCollectionsViewModel(
            this, workshop, workshopActions, dialogs, urls, workshopHistory, workshopUserCollections, workshopFavorites, workshopTitles, localCollections);
        LocalCollections = new LocalCollectionsViewModel(
            this, workshop, workshopActions, dialogs, clipboard, urls, workshopFavorites, workshopTitles, localCollections);
        WorkshopFavorites = new WorkshopFavoritesViewModel(
            this, workshop, workshopActions, dialogs, clipboard, urls, workshopFavorites, workshopTitles, localCollections);
        _error = new LocalizedText(text => ErrorText = text);

        // Never unsubscribed: these are app-lifetime singletons.
        localization.LanguageChanged += (_, _) => RenderTexts();
        installs.PropertyChanged += OnInstallsPropertyChanged;
        runState.PropertyChanged += OnRunStatePropertyChanged;
        orderWriter.Written += (_, result) => _ = ReportOrderWrittenAsync(result);
    }

    public InstalledModsViewModel Mods { get; }

    public ModCollectionsViewModel Collections { get; }

    public ModpacksViewModel Modpacks { get; }

    public WorkshopSubscriptionsViewModel WorkshopSubscriptions { get; }

    public WorkshopCollectionsViewModel WorkshopCollections { get; }

    public LocalCollectionsViewModel LocalCollections { get; }

    public WorkshopFavoritesViewModel WorkshopFavorites { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsGameFiles))]
    public partial ModsSection Section { get; set; }

    /// <summary>The section shows the game's mod files, whose load state and error are the page's own.</summary>
    public bool ShowsGameFiles => WorkshopSection is null || Section == ModsSection.Modpacks;

    /// <summary><c>null</c> when the install in use is none or a dedicated server.</summary>
    public PzInstall? Install => _installs.SelectedInstall is { Kind: PzInstallKind.Game } install ? install : null;

    public bool HasNoGameInstall => Install is null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanWrite), nameof(CanEdit))]
    public partial ModCollectionState? State { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanWrite))]
    public partial bool IsLoading { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanWrite))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    public partial bool IsBusy { get; private set; }

    [ObservableProperty] public partial string? ErrorText { get; private set; }

    /// <inheritdoc cref="GameRunState.IsGameRunning"/>
    public bool IsGameRunning => _runState.IsGameRunning;

    /// <summary>The game's files may be written now.</summary>
    public bool CanWrite => CanEdit && !IsLoading && !IsBusy;

    /// <summary>A reorder may be queued: it is written once nothing else is.</summary>
    internal bool CanEdit => Install is not null && State is not null && !IsGameRunning;

    internal IModCollectionService Service { get; }

    internal IWorkshopModsService WorkshopMods { get; }

    /// <summary>The Workshop section shown; <c>null</c> for a game-file section.</summary>
    private WorkshopSectionViewModel? WorkshopSection => Section switch
    {
        ModsSection.Mods or ModsSection.Collections => null,
        ModsSection.Modpacks => Modpacks,
        ModsSection.WorkshopSubscriptions => WorkshopSubscriptions,
        ModsSection.WorkshopCollections => WorkshopCollections,
        ModsSection.LocalCollections => LocalCollections,
        ModsSection.WorkshopFavorites => WorkshopFavorites,
    };

    private WorkshopSectionViewModel[] WorkshopSections => [Modpacks, WorkshopSubscriptions, WorkshopCollections, LocalCollections, WorkshopFavorites];

    // The view calls this on every load: the files may have changed in-game since the last visit.
    [RelayCommand]
    private Task ActivateAsync()
    {
        _activated = true;
        return Task.WhenAll(ReloadAsync(), ShowWorkshopSectionAsync());
    }

    private bool CanRefresh() => Install is not null && !IsBusy;

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private Task RefreshAsync()
    {
        DropSubscriptions();
        return WorkshopSection?.RefreshAsync() ?? ReloadAsync();
    }

    // Steam may have changed the subscriptions since the last visit.
    partial void OnSectionChanged(ModsSection value)
    {
        if (_activated)
        {
            _ = ShowWorkshopSectionAsync();
        }
    }

    private Task ShowWorkshopSectionAsync()
    {
        DropSubscriptions();
        return WorkshopSection?.ShowAsync() ?? Task.CompletedTask;
    }

    /// <summary>
    /// The install's subscriptions joined with its mods and with its presets as last read; Steam's files are read once until
    /// <see cref="DropSubscriptions"/>, so a list section switching entries only merges again.
    /// </summary>
    internal async Task<Result<WorkshopModSubscriptions>> GetSubscriptionsAsync(PzInstall install, CancellationToken ct)
    {
        if (_subscriptions is not { } shared || shared.Root != install.RootPath)
        {
            // Not the asking load's token: a newer load shares the read, and a superseded one only stops waiting for it.
            shared = new SharedSubscriptions(install.RootPath, WorkshopMods.GetSubscriptionsAsync(install.RootPath, ExtraGameArgs(install), CancellationToken.None));
            _subscriptions = shared;
        }

        var result = await shared.Read.WaitAsync(ct);
        if (!result.IsSuccess)
        {
            // The next load tries again.
            if (_subscriptions == shared)
            {
                _subscriptions = null;
            }

            return result;
        }

        return State is { } state ? result.Value.WithCollections(state.Collections) : result;
    }

    /// <summary>Steam's files may have changed: the next <see cref="GetSubscriptionsAsync"/> reads them again.</summary>
    internal void DropSubscriptions() => _subscriptions = null;

    internal bool PresetExists(string name) => State?.Collections.Any(collection => collection.Name == name) ?? false;

    /// <summary>Shows the Workshop collections section with this collection open.</summary>
    internal Task OpenWorkshopCollectionAsync(ulong id)
    {
        // Switched first: showing the section re-reads the collection open before, and the newer open supersedes that.
        Section = ModsSection.WorkshopCollections;
        return WorkshopCollections.LoadCollectionAsync(id);
    }

    /// <summary>Asks for a name, then saves a launcher mod collection of these mods and makes it the selected one.</summary>
    /// <returns>The name saved under; <c>null</c> when cancelled or the save failed.</returns>
    internal Task<string?> CreateCollectionAsync(string title, string initialName, IReadOnlyList<string> modIds) =>
        Collections.CreateAsync(title, initialName, modIds);

    /// <summary>Applies the install's mod collection as the enabled mod list and marks it current; a failure is shown.</summary>
    /// <returns><c>null</c> when it couldn't start.</returns>
    internal async Task<ModCollectionApplyOutcome?> ApplyCollectionAsync(string name)
    {
        ModCollectionApplyOutcome? outcome = null;
        await RunAsync(async (root, extraGameArgs) =>
        {
            outcome = await _applier.ApplyAsync(root, extraGameArgs, name);
            if (outcome is ModCollectionApplyOutcome.Applied && _defaults.Get(root) != name)
            {
                _defaults.Save(root, name);
            }
        });
        if (outcome is ModCollectionApplyOutcome.Failed failed)
        {
            await _dialogs.ShowErrorAsync(ErrorMessages.ToUserText(failed.Error));
        }

        return outcome;
    }

    // Every preset write goes through these, so the current preset and the modpacks' links are in step before the presets are read again.

    /// <summary>Saves the preset; with <paramref name="original"/>, under its new name. True when it was written; a failure is shown.</summary>
    internal Task<bool> SavePresetAsync(string name, IReadOnlyList<string> modIds, string? original = null) =>
        WriteAsync((root, extraGameArgs) => _presets.SaveAsync(root, extraGameArgs, name, modIds, original));

    /// <summary>Saves the presets in order, stopping at the first failure. True when all were written; a failure is shown.</summary>
    internal Task<bool> SavePresetsAsync(IReadOnlyList<SharedModCollection> presets) =>
        WriteAsync(async (root, extraGameArgs) =>
        {
            foreach (var preset in presets)
            {
                var saved = await _presets.SaveAsync(root, extraGameArgs, preset.Name, preset.ModIds);
                if (!saved.IsSuccess)
                {
                    return saved;
                }
            }

            return Result.Ok();
        });

    /// <inheritdoc cref="PresetWriter.DeleteAsync"/>
    internal Task<bool> DeletePresetAsync(string name, bool deleteUsers) =>
        WriteAsync((root, extraGameArgs) => _presets.DeleteAsync(root, extraGameArgs, name, deleteUsers));

    /// <inheritdoc cref="PresetWriter.RestoreAsync"/>
    internal Task<bool> RestorePresetAsync(string modpack, string name, IReadOnlyList<string> modIds) =>
        WriteAsync((root, extraGameArgs) => _presets.RestoreAsync(root, extraGameArgs, modpack, name, modIds));

    /// <summary>The shown install's modpacks linked to its preset of this name.</summary>
    internal IReadOnlyList<string> PresetUsers(string name) => Install is { } install ? _modpacks.UsersOfPreset(install.RootPath, name) : [];

    /// <summary>Every install's modpacks linked to the local collection, each with its install when that isn't the shown one.</summary>
    internal IReadOnlyList<string> LocalCollectionUsers(Guid id) => ModpackPrompts.UserNames(_modpacks, _modpacks.UsersOfLocalCollection(id), Install?.RootPath);

    /// <param name="deleteUsers">Every install's modpacks linked to it are deleted too; otherwise they keep their copy.</param>
    internal void DeleteLocalCollection(Guid id, bool deleteUsers)
    {
        if (deleteUsers)
        {
            _modpacks.DeleteUsersOfLocalCollection(id);
        }

        _localCollections.Delete(id);
    }

    internal async Task ReloadAsync()
    {
        var version = ++_loadVersion;
        if (Install is not { } install)
        {
            State = null;
            _error.Clear();
            IsLoading = false;
            return;
        }

        IsLoading = true;
        try
        {
            var result = await Service.GetAsync(install.RootPath, ExtraGameArgs(install));
            if (version != _loadVersion)
            {
                return;
            }

            if (!result.IsSuccess)
            {
                State = null;
                _error.Show(result.Error);
                return;
            }

            _error.Clear();

            // Before the state shows: its listeners read the modpacks.
            _modpacks.SyncPresets(install.RootPath, result.Value.Collections, result.Value.InstalledMods, Modpacks.SteamCollectionItems);
            State = result.Value;
        }
        finally
        {
            if (version == _loadVersion)
            {
                IsLoading = false;
            }
        }
    }

    /// <summary>Runs one change of the shown install's files after any queued reorder, then re-reads them. False when it couldn't start.</summary>
    internal async Task<bool> RunAsync(Func<string, string, Task> action)
    {
        if (!CanWrite || Install is not { } install)
        {
            return false;
        }

        IsBusy = true;
        try
        {
            var flushed = await _orderWriter.FlushAsync();
            if (!flushed.IsSuccess)
            {
                await _dialogs.ShowErrorAsync(ErrorMessages.ToUserText(flushed.Error));
                return false;
            }

            await action(install.RootPath, ExtraGameArgs(install));
            return true;
        }
        finally
        {
            IsBusy = false;
            await ReloadAsync();
        }
    }

    /// <summary><see cref="RunAsync"/> for one service write; a failure is shown. True when it was written.</summary>
    internal async Task<bool> WriteAsync(Func<string, string, Task<Result>> write)
    {
        var written = false;
        await RunAsync(async (root, extraGameArgs) =>
        {
            var result = await write(root, extraGameArgs);
            written = result.IsSuccess;
            if (!result.IsSuccess)
            {
                await _dialogs.ShowErrorAsync(ErrorMessages.ToUserText(result.Error));
            }
        });
        return written;
    }

    internal void ScheduleOrderSave(string name, IReadOnlyList<string> modIds)
    {
        if (Install is not { } install)
        {
            return;
        }

        // The queued write keeps its install, so a later install switch can't redirect it.
        _orderWriter.Queue(install.RootPath, ExtraGameArgs(install), name, modIds);
    }

    /// <summary>The queued order of this collection of the shown install, which a reload must not undo.</summary>
    internal IReadOnlyList<string>? PendingOrderOf(string name) =>
        Install is { } install ? _orderWriter.PendingOrder(install.RootPath, name) : null;

    private async Task ReportOrderWrittenAsync(Result result)
    {
        if (!result.IsSuccess)
        {
            await _dialogs.ShowErrorAsync(ErrorMessages.ToUserText(result.Error));
        }

        await ReloadAsync();
    }

    internal string ExtraGameArgs(PzInstall install) => _overrides.Get(install.RootPath).ExtraGameArgs;

    private void RenderTexts()
    {
        _error.Render();
        Collections.RenderTexts();
        foreach (var section in WorkshopSections)
        {
            section.RenderTexts();
        }
    }

    private void OnInstallsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not nameof(InstallsViewModel.SelectedInstall))
        {
            return;
        }

        State = null;
        _error.Clear();
        DropSubscriptions();
        OnPropertyChanged(nameof(Install));
        OnPropertyChanged(nameof(HasNoGameInstall));
        OnPropertyChanged(nameof(CanWrite));
        RefreshCommand.NotifyCanExecuteChanged();
        foreach (var section in WorkshopSections)
        {
            section.Clear();
        }

        if (_activated)
        {
            // Fire and forget: the loads report their own errors.
            _ = ReloadAsync();
            _ = ShowWorkshopSectionAsync();
        }
    }

    private void OnRunStatePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not nameof(GameRunState.IsGameRunning))
        {
            return;
        }

        OnPropertyChanged(nameof(IsGameRunning));
        OnPropertyChanged(nameof(CanWrite));
        if (!IsGameRunning && _activated)
        {
            // The game may have rewritten the files.
            _ = ReloadAsync();
        }
    }

    private sealed record SharedSubscriptions(string Root, Task<Result<WorkshopModSubscriptions>> Read);
}
