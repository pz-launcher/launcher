using System;
using System.ComponentModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using PzLauncher.Core.Installs;
using PzLauncher.Desktop.ViewModels.Installs;

namespace PzLauncher.Desktop.ViewModels.Home;

public partial class HomeViewModel : ViewModelBase
{
    private readonly InstallsViewModel _installs;

    public HomeViewModel(InstallsViewModel installs)
    {
        _installs = installs;

        // Never unsubscribed: both view models are app-lifetime singletons.
        installs.PropertyChanged += OnInstallsPropertyChanged;
    }

    public event EventHandler? SelectVersionRequested;

    public PzInstall? SelectedInstall => _installs.SelectedInstall;

    public bool IsScanning => SelectedInstall is null && _installs.IsScanning;

    public bool IsEmpty => SelectedInstall is null && !_installs.IsScanning;

    // Disabled until the launcher can start the game.
    public IRelayCommand LaunchCommand { get; } = new RelayCommand(() => { }, () => false);

    // The installs page owns the run-once rule; Home is the start page, so this is what scans at startup.
    [RelayCommand]
    private Task ActivateAsync() => _installs.ActivateCommand.ExecuteAsync(null);

    [RelayCommand]
    private void SelectVersion() => SelectVersionRequested?.Invoke(this, EventArgs.Empty);

    private void OnInstallsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(InstallsViewModel.SelectedInstall) or nameof(InstallsViewModel.IsScanning))
        {
            OnPropertyChanged(nameof(SelectedInstall));
            OnPropertyChanged(nameof(IsScanning));
            OnPropertyChanged(nameof(IsEmpty));
        }
    }
}
