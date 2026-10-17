using System.ComponentModel;
using System.Globalization;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PzLauncher.Core.Update;
using PzLauncher.Desktop.Lang;
using PzLauncher.Desktop.Localization;
using PzLauncher.Desktop.Services;

namespace PzLauncher.Desktop.ViewModels.Update;

/// <summary>The non-modal bar at the top of the main window that offers a downloaded or available update.</summary>
public sealed partial class UpdateBannerViewModel : ViewModelBase
{
    private readonly UpdateCoordinator _coordinator;
    private readonly GameRunState _runState;
    private readonly IUrlLauncher _urls;

    // "Later" hides the offer of this version only; a newer one shows the banner again.
    private SemVersion? _dismissed;

    public UpdateBannerViewModel(UpdateCoordinator coordinator, GameRunState runState, IUrlLauncher urls, ILocalizationService localization)
    {
        _coordinator = coordinator;
        _runState = runState;
        _urls = urls;
        Render();

        // Never unsubscribed: all of these live as long as the app.
        coordinator.PropertyChanged += OnCoordinatorChanged;
        runState.PropertyChanged += (_, _) => Render();
        localization.LanguageChanged += (_, _) => Render();
    }

    [ObservableProperty] public partial bool IsVisible { get; private set; }

    [ObservableProperty] public partial string? Text { get; private set; }

    /// <summary>Why the last restart attempt failed.</summary>
    [ObservableProperty] public partial string? ErrorText { get; private set; }

    /// <summary>A package is staged: "Restart and update" and "Later".</summary>
    [ObservableProperty] public partial bool OffersRestart { get; private set; }

    /// <summary>A release can't be installed in place: "Open download page" and "Later".</summary>
    [ObservableProperty] public partial bool OffersDownloadPage { get; private set; }

    /// <summary>Installed but not restarted: only "Dismiss".</summary>
    [ObservableProperty] public partial bool OffersDismissOnly { get; private set; }

    /// <summary>Explains the disabled restart button; <c>null</c> when it isn't blocked by a running game.</summary>
    [ObservableProperty] public partial string? RestartBlockedText { get; private set; }

    [RelayCommand(CanExecute = nameof(CanRestart))]
    private Task RestartAsync() => _coordinator.RestartAsync();

    private bool CanRestart() => _coordinator.CanRestart;

    [RelayCommand]
    private void Later()
    {
        _dismissed = OfferedVersion;
        Render();
    }

    [RelayCommand]
    private async Task OpenDownloadPageAsync()
    {
        if (_coordinator.Available is { } release)
        {
            await _urls.OpenAsync(release.ReleasePageUrl);
        }
    }

    private SemVersion? OfferedVersion =>
        _coordinator.Staged?.Version
        ?? (_coordinator.Status == UpdateStatus.Installed && _coordinator.RestartError is not null ? _coordinator.InstalledVersion : null)
        ?? _coordinator.Available?.Version;

    private void OnCoordinatorChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(UpdateCoordinator.CanRestart))
        {
            RestartCommand.NotifyCanExecuteChanged();
        }

        Render();
    }

    private void Render()
    {
        var offered = OfferedVersion;
        IsVisible = offered is not null && offered != _dismissed;
        OffersRestart = _coordinator.Staged is not null;
        OffersDismissOnly = !OffersRestart && _coordinator.Status == UpdateStatus.Installed;
        OffersDownloadPage = !OffersRestart && !OffersDismissOnly && _coordinator.Available is not null;
        Text = offered is null ? null
            : OffersRestart ? Format(UiText.Update_BannerReady, offered)
            : OffersDismissOnly ? Format(UiText.Update_StatusInstalled, offered)
            : Format(UiText.Update_BannerAvailable, offered);
        ErrorText = _coordinator.RestartError is { } error ? ErrorMessages.ToUserText(error) : null;
        RestartBlockedText = OffersRestart && _runState.IsGameRunning ? UiText.Update_RestartBlocked : null;
    }

    private static string Format(string format, SemVersion version) => string.Format(CultureInfo.CurrentCulture, format, version);
}
