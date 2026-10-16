using System;
using System.ComponentModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PzLauncher.Core.Installs;
using PzLauncher.Core.Launch;
using PzLauncher.Desktop.Localization;
using PzLauncher.Desktop.Services;
using PzLauncher.Desktop.ViewModels.Installs;

namespace PzLauncher.Desktop.ViewModels.Home;

public partial class HomeViewModel : ViewModelBase
{
    private readonly InstallsViewModel _installs;
    private readonly IGameLaunchService _launcher;
    private readonly IInstallOverridesService _overrides;
    private readonly IDialogService _dialogs;

    public HomeViewModel(
        InstallsViewModel installs,
        IGameLaunchService launcher,
        IInstallOverridesService overrides,
        IDialogService dialogs)
    {
        _installs = installs;
        _launcher = launcher;
        _overrides = overrides;
        _dialogs = dialogs;

        // Never unsubscribed: both view models are app-lifetime singletons.
        installs.PropertyChanged += OnInstallsPropertyChanged;
    }

    public event EventHandler? SelectVersionRequested;

    public event EventHandler? VersionSettingsRequested;

    public PzInstall? SelectedInstall => _installs.SelectedInstall;

    public bool IsScanning => SelectedInstall is null && _installs.IsScanning;

    public bool IsEmpty => SelectedInstall is null && !_installs.IsScanning;

    /// <summary>A game started from this launcher session is running, whichever install it came from.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LaunchCommand))]
    public partial bool IsGameRunning { get; private set; }

    private bool IsGameSelected => SelectedInstall is { Kind: PzInstallKind.Game };

    // The installs page owns the run-once rule; Home is the start page, so this is what scans at startup.
    [RelayCommand]
    private Task ActivateAsync() => _installs.ActivateCommand.ExecuteAsync(null);

    [RelayCommand]
    private void SelectVersion() => SelectVersionRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand(CanExecute = nameof(IsGameSelected))]
    private void VersionSettings() => VersionSettingsRequested?.Invoke(this, EventArgs.Empty);

    private bool CanLaunch() => IsGameSelected && !IsGameRunning;

    // Runs until the game exits, so the command stays busy for the whole session.
    [RelayCommand(CanExecute = nameof(CanLaunch))]
    private async Task LaunchAsync()
    {
        if (SelectedInstall is not { Kind: PzInstallKind.Game } install)
        {
            return;
        }

        var result = await _launcher.LaunchAsync(new LaunchGameRequest(install.RootPath, _overrides.Get(install.RootPath)));
        if (!result.IsSuccess)
        {
            await _dialogs.ShowErrorAsync(ErrorMessages.ToUserText(result.Error));
            return;
        }

        IsGameRunning = true;
        try
        {
            await _launcher.WaitForExitAsync();
        }
        finally
        {
            IsGameRunning = false;
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
            VersionSettingsCommand.NotifyCanExecuteChanged();
        }
    }
}
