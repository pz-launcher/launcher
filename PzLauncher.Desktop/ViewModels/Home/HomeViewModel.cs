using System;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PzLauncher.Core.Common;
using PzLauncher.Core.Installs;
using PzLauncher.Core.Launch;
using PzLauncher.Core.Mod.Zabric;
using PzLauncher.Core.Mod.ZombieBuddy;
using PzLauncher.Desktop.Localization;
using PzLauncher.Desktop.Services;
using PzLauncher.Desktop.ViewModels.Installs;
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
    private readonly IZabricService _zabric;
    private readonly IModCollectionDefaultsService _defaultCollections;
    private readonly ModCollectionApplier _applier;
    private readonly GameRunState _runState;

    public HomeViewModel(
        InstallsViewModel installs,
        IGameLaunchService launcher,
        IInstallOverridesService overrides,
        IDialogService dialogs,
        IZombieBuddyService zombieBuddy,
        IZombieBuddyChannelService channels,
        IZabricService zabric,
        IModCollectionDefaultsService defaultCollections,
        ModCollectionApplier applier,
        GameRunState runState)
    {
        _installs = installs;
        _launcher = launcher;
        _overrides = overrides;
        _dialogs = dialogs;
        _zombieBuddy = zombieBuddy;
        _channels = channels;
        _zabric = zabric;
        _defaultCollections = defaultCollections;
        _applier = applier;
        _runState = runState;

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

    private bool IsGameSelected => SelectedInstall is { Kind: PzInstallKind.Game };

    // The installs page owns the run-once rule; Home is the start page, so this is what scans at startup.
    [RelayCommand]
    private Task ActivateAsync() => _installs.ActivateCommand.ExecuteAsync(null);

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

        var overrides = _overrides.Get(install.RootPath);

        // A failed apply is reported, then the game starts with the enabled list it already has.
        var removeZombieBuddy = false;
        if (_defaultCollections.Get(install.RootPath) is { } collection)
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

        // A failed preload is reported, then the game starts as it would without the launcher.
        if (!removeZombieBuddy)
        {
            var preload = await _zabric.EnsurePreloadAsync(install.RootPath, overrides.ExtraGameArgs);
            if (!preload.IsSuccess)
            {
                await _dialogs.ShowErrorAsync(ErrorMessages.ToUserText(preload.Error));
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

    private void OnRunStatePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(GameRunState.IsGameRunning))
        {
            OnPropertyChanged(nameof(IsGameRunning));
            LaunchCommand.NotifyCanExecuteChanged();
        }
    }

    private void OnInstallsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(InstallsViewModel.SelectedInstall) or nameof(InstallsViewModel.IsScanning))
        {
            OnPropertyChanged(nameof(SelectedInstall));
            OnPropertyChanged(nameof(IsScanning));
            OnPropertyChanged(nameof(IsEmpty));
            LaunchCommand.NotifyCanExecuteChanged();
            LaunchSettingsCommand.NotifyCanExecuteChanged();
        }
    }
}
