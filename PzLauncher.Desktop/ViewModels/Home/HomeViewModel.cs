using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PzLauncher.Core.Common;
using PzLauncher.Core.Installs;
using PzLauncher.Core.Launch;
using PzLauncher.Core.Mod.Collections;
using PzLauncher.Core.Mod.ZombieBuddy;
using PzLauncher.Core.Steam.Workshop.Actions;
using PzLauncher.Core.Steam.Workshop;
using PzLauncher.Desktop.Lang;
using PzLauncher.Desktop.Localization;
using PzLauncher.Desktop.Services.Launch;
using PzLauncher.Desktop.Services.Mods;
using PzLauncher.Desktop.Services.Shell;
using PzLauncher.Desktop.Services.ZombieBuddy;
using PzLauncher.Desktop.ViewModels.Installs;
using PzLauncher.Desktop.ViewModels.Mods.Workshop;
using PzLauncher.Desktop.ViewModels.Mods;
using PzLauncher.Desktop.ViewModels.ZombieBuddy;

namespace PzLauncher.Desktop.ViewModels.Home;

public partial class HomeViewModel : ViewModelBase
{
    private readonly InstallsViewModel _installs;
    private readonly IGameLaunchService _launcher;
    private readonly IInstallOverridesService _overrides;
    private readonly IDialogService _dialogs;
    private readonly IZombieBuddyService _zombieBuddy;
    private readonly IZombieBuddyChannelService _channels;
    private readonly IZombieBuddyModsService _zombieBuddyMods;
    private readonly IModCollectionService _collections;
    private readonly IModCollectionDefaultsService _defaultCollections;
    private readonly ModCollectionApplier _applier;
    private readonly IModCollectionOrderWriter _orderWriter;
    private readonly WorkshopLaunchCheck _workshopCheck;
    private readonly IWorkshopService _workshop;
    private readonly IWorkshopActionRunner _workshopActions;
    private readonly GameRunState _runState;

    private CollectionChoice _selectedCollection = CollectionChoice.None;
    private bool _activated;

    // A newer collection load supersedes one still in flight.
    private int _collectionsVersion;

    public HomeViewModel(
        InstallsViewModel installs,
        IGameLaunchService launcher,
        IInstallOverridesService overrides,
        IDialogService dialogs,
        IZombieBuddyService zombieBuddy,
        IZombieBuddyChannelService channels,
        IZombieBuddyModsService zombieBuddyMods,
        IModCollectionService collections,
        IModCollectionDefaultsService defaultCollections,
        ModCollectionApplier applier,
        IModCollectionOrderWriter orderWriter,
        WorkshopLaunchCheck workshopCheck,
        IWorkshopService workshop,
        IWorkshopActionRunner workshopActions,
        GameRunState runState)
    {
        _installs = installs;
        _launcher = launcher;
        _overrides = overrides;
        _dialogs = dialogs;
        _zombieBuddy = zombieBuddy;
        _channels = channels;
        _zombieBuddyMods = zombieBuddyMods;
        _collections = collections;
        _defaultCollections = defaultCollections;
        _applier = applier;
        _orderWriter = orderWriter;
        _workshopCheck = workshopCheck;
        _workshop = workshop;
        _workshopActions = workshopActions;
        _runState = runState;
        CollectionChoices = [CollectionChoice.None];

        // Never unsubscribed: all of these are app-lifetime singletons.
        installs.PropertyChanged += OnInstallsPropertyChanged;
        runState.PropertyChanged += OnRunStatePropertyChanged;
    }

    public event EventHandler? ChooseInstallRequested;

    public event EventHandler<PzInstall>? LaunchSettingsRequested;

    public PzInstall? SelectedInstall => _installs.SelectedInstall;

    public bool IsScanning => SelectedInstall is null && _installs.IsScanning;

    public bool IsEmpty => SelectedInstall is null && !_installs.IsScanning;

    /// <inheritdoc cref="GameRunState.IsGameRunning"/>
    public bool IsGameRunning => _runState.IsGameRunning;

    public bool IsGameSelected => SelectedInstall is { Kind: PzInstallKind.Game };

    /// <summary>The selected install's collections in file order, then <see cref="CollectionChoice.None"/>.</summary>
    [ObservableProperty] public partial IReadOnlyList<CollectionChoice> CollectionChoices { get; private set; }

    // The combo box pushes null while its items are replaced; that must not clear the choice.
    /// <summary>The current collection, applied before each launch. Picking one only saves the choice.</summary>
    public CollectionChoice? CollectionSelection
    {
        get => _selectedCollection;
        set
        {
            if (value is null || value == _selectedCollection || SelectedInstall is not { Kind: PzInstallKind.Game } install)
            {
                return;
            }

            _selectedCollection = value;
            OnPropertyChanged();
            if (value.Name is { } name)
            {
                _defaultCollections.Save(install.RootPath, name);
            }
            else
            {
                _defaultCollections.Clear(install.RootPath);
            }
        }
    }

    public bool CanChooseCollection => IsGameSelected && !IsGameRunning;

    // The installs page owns the run-once rule; Home is the start page, so this is what scans at startup.
    // Every visit re-reads the collections: they and the current one may have changed on the mods page or in-game.
    [RelayCommand]
    private async Task ActivateAsync()
    {
        await _installs.ActivateCommand.ExecuteAsync(null);
        _activated = true;
        await LoadCollectionsAsync();
    }

    [RelayCommand]
    private Task RefreshCollectionsAsync() => LoadCollectionsAsync();

    [RelayCommand]
    private void ChooseInstall() => ChooseInstallRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand(CanExecute = nameof(IsGameSelected))]
    private void LaunchSettings()
    {
        if (SelectedInstall is { Kind: PzInstallKind.Game } install)
        {
            LaunchSettingsRequested?.Invoke(this, install);
        }
    }

    private bool CanLaunch() => IsGameSelected && !IsGameRunning;

    // Runs until the game exits, so the command stays busy for the whole session.
    [RelayCommand(CanExecute = nameof(CanLaunch))]
    private async Task LaunchAsync()
    {
        if (SelectedInstall is not { Kind: PzInstallKind.Game } install)
        {
            return;
        }

        // A reorder still waiting for its delay must be on disk before the default collection is applied.
        var flushed = await _orderWriter.FlushAsync();
        if (!flushed.IsSuccess)
        {
            await _dialogs.ShowErrorAsync(ErrorMessages.ToUserText(flushed.Error));
        }

        var overrides = _overrides.Get(install.RootPath);
        var collection = _defaultCollections.Get(install.RootPath);
        if (!await UpdateWorkshopItemsAsync(install.RootPath, overrides.ExtraGameArgs, collection))
        {
            return;
        }

        // A failed apply is reported, then the game starts with the enabled list it already has.
        var removeZombieBuddy = false;
        if (collection is not null)
        {
            switch (await _applier.ApplyAsync(install.RootPath, overrides.ExtraGameArgs, collection))
            {
                case ModCollectionApplyOutcome.Cancelled:
                    return;
                case ModCollectionApplyOutcome.Failed failed:
                    await _dialogs.ShowErrorAsync(ErrorMessages.ToUserText(failed.Error));
                    break;
                case ModCollectionApplyOutcome.Applied applied:
                    removeZombieBuddy = !applied.Result.EnabledIds.Contains(ZombieBuddyMod.Id, StringComparer.Ordinal);
                    break;
            }
        }

        if (!await PrepareZombieBuddyAsync(install.RootPath, removeZombieBuddy))
        {
            return;
        }

        // A failed mod setup is reported, then the game starts as it would without the launcher.
        if (!removeZombieBuddy)
        {
            foreach (var error in await _zombieBuddyMods.PrepareLaunchAsync(install.RootPath, overrides.ExtraGameArgs))
            {
                await _dialogs.ShowErrorAsync(ErrorMessages.ToUserText(error));
            }
        }

        var result = await _launcher.LaunchAsync(new LaunchGameRequest(install.RootPath, overrides));
        if (!result.IsSuccess)
        {
            await _dialogs.ShowErrorAsync(ErrorMessages.ToUserText(result.Error));
            return;
        }

        _runState.IsGameRunning = true;
        Result exit;
        try
        {
            exit = await _launcher.WaitForExitAsync();
        }
        finally
        {
            _runState.IsGameRunning = false;
        }

        if (!exit.IsSuccess)
        {
            await _dialogs.ShowErrorAsync(ErrorMessages.ToUserText(exit.Error));
        }
    }

    // False when the user cancelled the launch. Runs before the apply, so a cancel leaves the enabled list as it was.
    private async Task<bool> UpdateWorkshopItemsAsync(string installRoot, string extraGameArgs, string? collection)
    {
        var outdated = await _workshopCheck.FindOutdatedAsync(installRoot, extraGameArgs, collection);
        if (outdated.Count == 0)
        {
            return true;
        }

        var prompt = new ConfirmPrompt(
            UiText.WorkshopLaunch_Title,
            string.Format(CultureInfo.CurrentCulture, UiText.WorkshopLaunch_Message, outdated.Count),
            UiText.WorkshopLaunch_Update,
            UiText.Dialog_Cancel)
        {
            Items = [.. outdated.Select(WorkshopActionFlow.NameWithId)],
        };
        switch (await _dialogs.ChooseAsync(prompt, UiText.WorkshopLaunch_LaunchNow))
        {
            case ConfirmChoice.Confirm:
                break;
            case ConfirmChoice.Alternative:
                return true;
            case ConfirmChoice.Cancel:
                return false;
        }

        // The items are on no page here, so no row shows their download.
        var items = WorkshopActionFlow.ActionItems(outdated);
        await WorkshopActionFlow.RunAsync(
            _workshopActions, _workshop, _dialogs, installRoot, WorkshopAction.Update, items, () => []);

        // Closing the progress dialog early leaves the unfinished items short of Done too.
        var names = outdated.ToDictionary(item => item.Id, WorkshopActionFlow.NameWithId);
        List<string> notUpdated = [.. items.Where(item => item.State != WorkshopActionItemState.Done).Select(item => names[item.Id])];
        return notUpdated.Count == 0 || await _dialogs.ConfirmAsync(
            new ConfirmPrompt(
                UiText.WorkshopLaunch_FailedTitle,
                string.Format(CultureInfo.CurrentCulture, UiText.WorkshopLaunch_FailedMessage, notUpdated.Count),
                UiText.WorkshopLaunch_LaunchNow,
                UiText.Dialog_Cancel)
            {
                Items = notUpdated,
            });
    }

    // False when the user cancelled the setup dialog. A status error falls through: the launch reports the same config problem.
    // The applied default collection decides when it leaves ZombieBuddy out: then only leftovers are removed.
    private async Task<bool> PrepareZombieBuddyAsync(string installRoot, bool remove)
    {
        var status = await _zombieBuddy.GetStatusAsync(installRoot);
        if (!status.IsSuccess)
        {
            return true;
        }

        ZombieBuddySetupMode? needed = remove
            ? status.Value.HasLeftovers ? ZombieBuddySetupMode.Remove : null
            : status.Value.NeededAction switch
            {
                ZombieBuddyAction.None => null,
                ZombieBuddyAction.Initialize => ZombieBuddySetupMode.Initialize,
                ZombieBuddyAction.Remove => ZombieBuddySetupMode.Remove,
            };
        if (needed is not { } mode)
        {
            return true;
        }

        var channel = _channels.EffectiveItem(installRoot, status.Value.Subscribed)?.Channel ?? ZombieBuddyChannel.Stable;
        var setup = new ZombieBuddySetupViewModel(_zombieBuddy, mode, installRoot, channel, offersLaunch: true);
        return await _dialogs.ShowZombieBuddySetupAsync(setup);
    }

    private async Task LoadCollectionsAsync()
    {
        var version = ++_collectionsVersion;
        if (SelectedInstall is not { Kind: PzInstallKind.Game } install)
        {
            ShowCollections(null, []);
            return;
        }

        var read = await _collections.GetAsync(install.RootPath, _overrides.Get(install.RootPath).ExtraGameArgs);
        if (version != _collectionsVersion)
        {
            return;
        }

        if (!read.IsSuccess)
        {
            // Only "no collection" is offered, but the saved one stays: a launch reports why it can't be applied.
            ShowCollections(install, []);
            return;
        }

        var names = read.Value.Collections.Select(collection => collection.Name).ToList();

        // Deleted outside the launcher (in-game): forget it, so the launch doesn't report it as not found every time.
        if (_defaultCollections.Get(install.RootPath) is { } saved && !names.Contains(saved, StringComparer.Ordinal))
        {
            _defaultCollections.Clear(install.RootPath);
        }

        ShowCollections(install, names);
    }

    private void ShowCollections(PzInstall? install, IReadOnlyList<string> names)
    {
        CollectionChoice[] choices = [.. names.Select(name => new CollectionChoice(name)), CollectionChoice.None];
        if (!choices.SequenceEqual(CollectionChoices))
        {
            CollectionChoices = choices;
        }

        var saved = install is null ? null : _defaultCollections.Get(install.RootPath);
        _selectedCollection = saved is not null && names.Contains(saved, StringComparer.Ordinal) ? new CollectionChoice(saved) : CollectionChoice.None;
        OnPropertyChanged(nameof(CollectionSelection));
    }

    private void OnRunStatePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not nameof(GameRunState.IsGameRunning))
        {
            return;
        }

        OnPropertyChanged(nameof(IsGameRunning));
        OnPropertyChanged(nameof(CanChooseCollection));
        LaunchCommand.NotifyCanExecuteChanged();
        if (!IsGameRunning && _activated)
        {
            // The game may have rewritten the collections file. Fire and forget: a failed read only empties the list.
            _ = LoadCollectionsAsync();
        }
    }

    private void OnInstallsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(InstallsViewModel.SelectedInstall) or nameof(InstallsViewModel.IsScanning))
        {
            OnPropertyChanged(nameof(SelectedInstall));
            OnPropertyChanged(nameof(IsScanning));
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(IsGameSelected));
            OnPropertyChanged(nameof(CanChooseCollection));
            LaunchCommand.NotifyCanExecuteChanged();
            LaunchSettingsCommand.NotifyCanExecuteChanged();
        }

        if (e.PropertyName is nameof(InstallsViewModel.SelectedInstall))
        {
            // The old install's names must never be picked for the new one.
            ShowCollections(null, []);
            if (_activated)
            {
                _ = LoadCollectionsAsync();
            }
        }
    }
}
