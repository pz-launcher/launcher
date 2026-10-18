using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PzLauncher.Core.Mod.ZombieBuddy;
using PzLauncher.Desktop.Localization;
using PzLauncher.Desktop.Services.Launch;
using PzLauncher.Desktop.Services.Shell;
using PzLauncher.Desktop.Services.ZombieBuddy;
using PzLauncher.Desktop.ViewModels.ZombieBuddy;

namespace PzLauncher.Desktop.ViewModels.LaunchSettings;

/// <summary>The ZombieBuddy section of the launch settings page, for the game install the page loaded.</summary>
public sealed partial class LaunchSettingsZombieBuddyViewModel : ViewModelBase
{
    private readonly IZombieBuddyService _zombieBuddy;
    private readonly IZombieBuddyChannelService _channels;
    private readonly GameRunState _runState;
    private readonly IDialogService _dialogs;
    private readonly LocalizedText _error;

    // Set while the fields are filled from the status, so filling them doesn't write.
    private bool _applying;

    // Null while nothing is loaded.
    private string? _installRoot;

    // Null while the section is hidden.
    private ZombieBuddyStatus? _status;

    // A newer load supersedes one still in flight.
    private int _version;

    public LaunchSettingsZombieBuddyViewModel(
        IZombieBuddyService zombieBuddy,
        IZombieBuddyChannelService channels,
        GameRunState runState,
        IDialogService dialogs)
    {
        _zombieBuddy = zombieBuddy;
        _channels = channels;
        _runState = runState;
        _dialogs = dialogs;
        _error = new LocalizedText(text => ErrorText = text);

        // Never unsubscribed: the run state is an app-lifetime singleton, and so is the page owning this section.
        runState.PropertyChanged += OnRunStatePropertyChanged;
    }

    /// <summary>The card is shown: a supported install that is subscribed or has leftovers.</summary>
    public bool IsShown => _status is not null;

    public bool IsSubscribed => _status is { Subscribed.Count: > 0 };

    public bool IsInitialized => _status is { IsInitialized: true };

    public ZombieBuddyState State =>
        !IsSubscribed ? ZombieBuddyState.Leftovers
        : IsInitialized ? ZombieBuddyState.Initialized
        : ZombieBuddyState.NotInitialized;

    /// <summary>Only an unsubscribed install with leftovers offers removing them.</summary>
    public bool CanOfferRemove => _status is { Subscribed.Count: 0, HasLeftovers: true };

    /// <summary>Both channels are subscribed, so the user picks one.</summary>
    public bool HasChannelChoice => _status is { Subscribed.Count: > 1 };

    /// <summary>The item initialize copies from.</summary>
    public ZombieBuddyItem? Source =>
        _status?.Subscribed.FirstOrDefault(item => item.Channel == SelectedChannel)
        ?? _status?.Subscribed.FirstOrDefault();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Source))]
    public partial ZombieBuddyChannel SelectedChannel { get; set; }

    /// <summary>Applies to the json at once; the switch is shown only when initialized.</summary>
    [ObservableProperty] public partial bool IsAllowAll { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    [NotifyCanExecuteChangedFor(nameof(InitializeCommand), nameof(RemoveCommand))]
    public partial bool IsApplyingAllowAll { get; private set; }

    /// <summary>The game holds the files open and reads the policy only at start, so nothing changes while it runs.</summary>
    public bool CanEdit => !_runState.IsGameRunning && !IsApplyingAllowAll;

    public bool IsGameRunning => _runState.IsGameRunning;

    [ObservableProperty] public partial string? ErrorText { get; private set; }

    /// <summary>Hides the section and drops a load still running.</summary>
    public void Clear()
    {
        _version++;
        _installRoot = null;
        Apply(null);
        _error.Clear();
    }

    public Task LoadAsync(string installRoot, CancellationToken ct)
    {
        _installRoot = installRoot;
        return RefreshAsync(installRoot, ct);
    }

    public void RenderTexts() => _error.Render();

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private Task InitializeAsync() => RunSetupAsync(ZombieBuddySetupMode.Initialize);

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private Task RemoveAsync() => RunSetupAsync(ZombieBuddySetupMode.Remove);

    private async Task RunSetupAsync(ZombieBuddySetupMode mode)
    {
        if (_installRoot is not { } root || _status is null)
        {
            return;
        }

        _error.Clear();
        var channel = Source?.Channel ?? ZombieBuddyChannel.Stable;
        await _dialogs.ShowZombieBuddySetupAsync(new ZombieBuddySetupViewModel(_zombieBuddy, mode, root, channel, offersLaunch: false));
        if (_installRoot == root)
        {
            await RefreshAsync(root, CancellationToken.None);
        }
    }

    private async Task RefreshAsync(string installRoot, CancellationToken ct)
    {
        var version = ++_version;
        var status = await _zombieBuddy.GetStatusAsync(installRoot, ct);
        if (version == _version)
        {
            // An unreadable json is already shown by the page, and the section can't work without it.
            Apply(status.IsSuccess ? status.Value : null);
        }
    }

    private void Apply(ZombieBuddyStatus? status)
    {
        _status = status is { IsSupported: true } shown && (shown.Subscribed.Count > 0 || shown.HasLeftovers) ? shown : null;
        _applying = true;
        try
        {
            SelectedChannel = _installRoot is { } root && _status is { } current
                ? _channels.EffectiveItem(root, current.Subscribed)?.Channel ?? ZombieBuddyChannel.Stable
                : ZombieBuddyChannel.Stable;
            IsAllowAll = _status?.IsAllowAll ?? false;
        }
        finally
        {
            _applying = false;
        }

        OnPropertyChanged(nameof(IsShown));
        OnPropertyChanged(nameof(IsSubscribed));
        OnPropertyChanged(nameof(IsInitialized));
        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(CanOfferRemove));
        OnPropertyChanged(nameof(HasChannelChoice));
        OnPropertyChanged(nameof(Source));
    }

    partial void OnSelectedChannelChanged(ZombieBuddyChannel value)
    {
        if (!_applying && _installRoot is { } root && _status is not null)
        {
            _channels.Save(root, value);
        }
    }

    partial void OnIsAllowAllChanged(bool value)
    {
        if (!_applying && _installRoot is { } root && _status is not null)
        {
            _ = SetAllowAllAsync(root, value);
        }
    }

    private async Task SetAllowAllAsync(string installRoot, bool allowAll)
    {
        IsApplyingAllowAll = true;
        _error.Clear();
        try
        {
            var result = await _zombieBuddy.SetAllowAllAsync(installRoot, allowAll);
            if (!result.IsSuccess && _installRoot == installRoot)
            {
                // The switch goes back to what the json holds now.
                await RefreshAsync(installRoot, CancellationToken.None);
                _error.Show(result.Error);
            }
        }
        finally
        {
            IsApplyingAllowAll = false;
        }
    }

    private void OnRunStatePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(GameRunState.IsGameRunning))
        {
            OnPropertyChanged(nameof(IsGameRunning));
            OnPropertyChanged(nameof(CanEdit));
            InitializeCommand.NotifyCanExecuteChanged();
            RemoveCommand.NotifyCanExecuteChanged();
        }
    }
}
