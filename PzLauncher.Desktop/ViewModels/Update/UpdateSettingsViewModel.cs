using System.ComponentModel;
using System.Globalization;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PzLauncher.Core.Update.Releases;
using PzLauncher.Core.Update;
using PzLauncher.Desktop.Lang;
using PzLauncher.Desktop.Localization;
using PzLauncher.Desktop.Services.Update;

namespace PzLauncher.Desktop.ViewModels.Update;

/// <summary>The Settings "Updates" section over the app-wide <see cref="UpdateCoordinator"/>.</summary>
public sealed partial class UpdateSettingsViewModel : ViewModelBase
{
    private readonly UpdateCoordinator _coordinator;

    public UpdateSettingsViewModel(UpdateCoordinator coordinator, ILocalizationService localization)
    {
        _coordinator = coordinator;
        RenderTexts();

        // Never unsubscribed: the coordinator, the localization service and this view model live as long as the app.
        coordinator.PropertyChanged += OnCoordinatorChanged;
        localization.LanguageChanged += (_, _) => RenderTexts();
    }

    public UpdateSource Source
    {
        get => _coordinator.Source;
        set => _coordinator.Source = value;
    }

    public bool AutoCheck
    {
        get => _coordinator.AutoCheck;
        set => _coordinator.AutoCheck = value;
    }

    [ObservableProperty] public partial string CurrentVersionText { get; private set; } = "";

    /// <summary><c>null</c> while nothing has happened yet.</summary>
    [ObservableProperty] public partial string? StatusText { get; private set; }

    [ObservableProperty] public partial bool IsStatusError { get; private set; }

    public bool IsBusy => _coordinator.IsBusy;

    /// <summary>Indeterminate while checking, or while downloading a package of unknown size.</summary>
    public bool IsProgressIndeterminate => _coordinator.Status == UpdateStatus.Checking || _coordinator.DownloadPercent is null;

    public int ProgressPercent => _coordinator.DownloadPercent ?? 0;

    [RelayCommand(CanExecute = nameof(CanCheckNow))]
    private Task CheckNowAsync() => _coordinator.CheckNowAsync();

    private bool CanCheckNow() => _coordinator.CanCheck;

    private void OnCoordinatorChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(UpdateCoordinator.Source):
                OnPropertyChanged(nameof(Source));
                break;
            case nameof(UpdateCoordinator.AutoCheck):
                OnPropertyChanged(nameof(AutoCheck));
                break;
            case nameof(UpdateCoordinator.CanCheck):
                CheckNowCommand.NotifyCanExecuteChanged();
                break;
            default:
                OnPropertyChanged(nameof(IsBusy));
                OnPropertyChanged(nameof(IsProgressIndeterminate));
                OnPropertyChanged(nameof(ProgressPercent));
                RenderTexts();
                break;
        }
    }

    private void RenderTexts()
    {
        CurrentVersionText = Format(UiText.Settings_UpdatesCurrentVersion, _coordinator.CurrentVersion);
        IsStatusError = _coordinator.Status == UpdateStatus.Failed;
        StatusText = _coordinator.Status switch
        {
            UpdateStatus.Idle => null,
            UpdateStatus.Checking => UiText.Update_StatusChecking,
            UpdateStatus.UpToDate => UiText.Update_StatusUpToDate,
            UpdateStatus.Downloading => _coordinator.Release is not { } release ? null
                : _coordinator.DownloadPercent is { } percent
                    ? Format(UiText.Update_StatusDownloadingPercent, release.Version, DisplayNames.Of(release.Source), percent)
                    : Format(UiText.Update_StatusDownloading, release.Version, DisplayNames.Of(release.Source)),
            UpdateStatus.Ready => _coordinator.Staged is { } staged ? Format(UiText.Update_StatusReady, staged.Version) : null,
            UpdateStatus.Available => _coordinator.Available is { } available ? Format(UiText.Update_StatusAvailable, available.Version) : null,
            UpdateStatus.Installed => _coordinator.InstalledVersion is { } installed ? Format(UiText.Update_StatusInstalled, installed) : null,
            UpdateStatus.Failed => _coordinator.Error is { } error ? ErrorMessages.ToUserText(error) : null,
        };
    }

    private static string Format(string format, params object[] args) => string.Format(CultureInfo.CurrentCulture, format, args);
}
