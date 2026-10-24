using System;
using System.Globalization;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using PzLauncher.Core.Common;
using PzLauncher.Core.Platform;
using PzLauncher.Core.Steam.LaunchHook;
using PzLauncher.Core.Steam;
using PzLauncher.Desktop.Lang;
using PzLauncher.Desktop.Localization;
using PzLauncher.Desktop.Services.Preferences;
using PzLauncher.Desktop.Services.Shell.Dialogs;
using PzLauncher.Desktop.Services.Shell;
using PzLauncher.Desktop.Services.Steam;

namespace PzLauncher.Desktop.ViewModels.Settings;

/// <summary>The Settings "Steam" section and the startup reminder; Windows only.</summary>
public sealed partial class SteamSettingsViewModel : ViewModelBase
{
    private readonly SteamLaunchHookFlow _flow;
    private readonly IPreferenceStore _preferences;
    private readonly IDialogService _dialogs;

    // Set after the saved choice is shown, so showing it doesn't save it again.
    private readonly bool _loaded;

    // Set while the switch shows the state read from Steam, so showing it doesn't apply it.
    private bool _showingState;

    private bool _reminded;
    private Activity _activity;

    /// <summary><c>null</c> until first read.</summary>
    private Result<SteamLaunchHookStatus>? _state;

    public SteamSettingsViewModel(
        SteamLaunchHookFlow flow,
        IPreferenceStore preferences,
        UserPreferences saved,
        IDialogService dialogs,
        IPlatformEnvironment platform,
        ILocalizationService localization)
    {
        _flow = flow;
        _preferences = preferences;
        _dialogs = dialogs;
        IsSupported = platform.Os == PlatformKind.Windows;
        RemindAtStartup = saved.SteamHookReminder ?? true;
        _loaded = true;

        // Never unsubscribed: the localization service and this view model live as long as the app.
        localization.LanguageChanged += (_, _) => RenderTexts();
    }

    private enum Activity
    {
        None,
        Loading,
        Applying,
    }

    /// <summary>Steam's launch options are only managed on Windows; elsewhere the section is hidden.</summary>
    public bool IsSupported { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanToggle))]
    public partial bool IsBusy { get; private set; }

    /// <summary>A Steam account with Project Zomboid settings was found.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanToggle))]
    public partial bool IsAvailable { get; private set; }

    public bool CanToggle => IsAvailable && !IsBusy;

    /// <summary>Two-way: the user's toggle applies the change, then shows what Steam holds.</summary>
    [ObservableProperty] public partial bool IsHookOn { get; set; }

    [ObservableProperty] public partial bool RemindAtStartup { get; set; }

    [ObservableProperty] public partial string? AccountText { get; private set; }

    /// <summary>Set while Steam opens a launcher in another folder.</summary>
    [ObservableProperty] public partial string? OtherLauncherText { get; private set; }

    /// <summary>What is in progress, or why the switch is unavailable; <c>null</c> otherwise.</summary>
    [ObservableProperty] public partial string? StatusText { get; private set; }

    [ObservableProperty] public partial bool IsStatusError { get; private set; }

    /// <summary>Re-reads Steam's launch options; each time the section opens.</summary>
    public async Task LoadAsync()
    {
        if (!IsSupported || IsBusy)
        {
            return;
        }

        await RunAsync(Activity.Loading, () => _flow.GetStatusAsync());
    }

    /// <summary>Once, after the main window shows: offers to turn the hook on while it is off. Status errors are only logged (by Core).</summary>
    public async Task RemindAtStartupAsync()
    {
        if (!IsSupported || !RemindAtStartup || _reminded)
        {
            return;
        }

        _reminded = true;
        var status = await _flow.GetStatusAsync();
        if (!status.IsSuccess || status.Value.State == SteamLaunchHookState.On)
        {
            return;
        }

        Show(status);
        var prompt = status.Value is { State: SteamLaunchHookState.OtherLauncher, HookedPath: { } other }
            ? new ConfirmPrompt(UiText.Steam_ReminderOtherTitle, UiText.Steam_ReminderOtherMessage, UiText.Steam_ReminderSwitch, UiText.Steam_ReminderLater) { Items = [other] }
            : new ConfirmPrompt(UiText.Steam_ReminderTitle, UiText.Steam_ReminderMessage, UiText.Steam_ReminderEnable, UiText.Steam_ReminderLater);
        var answer = await _dialogs.ConfirmWithOptionAsync(prompt, UiText.Steam_ReminderDontRemind);
        if (answer.OptionChecked)
        {
            RemindAtStartup = false;
        }

        if (answer.Confirmed && !IsBusy)
        {
            await RunAsync(Activity.Applying, () => _flow.SetAsync(enabled: true));
        }
    }

    partial void OnIsHookOnChanged(bool value)
    {
        if (!_showingState && !IsBusy)
        {
            // Fire and forget: the flow reports its own errors, and the switch then shows Steam's real state.
            _ = RunAsync(Activity.Applying, () => _flow.SetAsync(value));
        }
    }

    partial void OnRemindAtStartupChanged(bool value)
    {
        if (_loaded)
        {
            // Never throws for I/O; a failed save keeps the choice for this session.
            _ = _preferences.SaveSteamHookReminderAsync(value);
        }
    }

    private async Task RunAsync(Activity activity, Func<Task<Result<SteamLaunchHookStatus>>> operation)
    {
        _activity = activity;
        IsBusy = true;
        RenderTexts();
        try
        {
            _state = await operation();
        }
        finally
        {
            _activity = Activity.None;
            IsBusy = false;
            RenderTexts();
        }
    }

    private void Show(Result<SteamLaunchHookStatus> state)
    {
        _state = state;
        RenderTexts();
    }

    private void RenderTexts()
    {
        var failure = _state is { } state && !state.IsSuccess ? state.Error : null;
        var status = _state is { } read && read.IsSuccess ? read.Value : null;
        IsAvailable = status is not null;
        AccountText = status is null ? null : Format(UiText.Steam_Account, status.Account.PersonaName);
        OtherLauncherText = status is { State: SteamLaunchHookState.OtherLauncher, HookedPath: { } path } ? Format(UiText.Steam_OtherLauncher, path) : null;

        // While applying, the switch keeps showing what the user asked for.
        if (_activity != Activity.Applying)
        {
            _showingState = true;
            IsHookOn = status?.State == SteamLaunchHookState.On;
            _showingState = false;
        }

        IsStatusError = _activity == Activity.None && failure is not null;
        StatusText = _activity switch
        {
            Activity.Loading => UiText.Steam_Loading,
            Activity.Applying => UiText.Steam_Applying,
            Activity.None => failure is null ? null : ErrorMessages.ToUserText(failure),
        };
    }

    private static string Format(string format, string value) => string.Format(CultureInfo.CurrentCulture, format, value);
}
