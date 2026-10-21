using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using PzLauncher.Core.Platform;
using PzLauncher.Desktop.Localization;
using PzLauncher.Desktop.Services.Preferences;
using PzLauncher.Desktop.Services.Shell;

namespace PzLauncher.Desktop.Services.Launch;

/// <summary>App-lifetime: the saved <see cref="AfterLaunchAction"/>, shared by the launch's <see cref="AfterLaunchFlow"/> and the Settings "General" section.</summary>
public sealed partial class AfterLaunchSettings(IPreferenceStore preferences, UserPreferences saved, IPlatformEnvironment platform) : ObservableObject
{
    /// <summary><c>null</c> until the user has chosen; setting a choice saves it.</summary>
    [ObservableProperty] public partial AfterLaunchAction? Action { get; set; } = saved.AfterLaunch;

    /// <summary>Many Linux desktops show no tray icon, which would leave a window hidden to the tray no way back.</summary>
    public bool OffersTray => platform.Os != PlatformKind.Linux;

    /// <summary>Every offered choice in display order, with its text in the current language.</summary>
    public IReadOnlyList<ChoiceOption<AfterLaunchAction>> Options() =>
    [
        .. Enum.GetValues<AfterLaunchAction>()
            .Where(action => action != AfterLaunchAction.MinimizeToTray || OffersTray)
            .Select(action => new ChoiceOption<AfterLaunchAction>(action, DisplayNames.Of(action))),
    ];

    partial void OnActionChanged(AfterLaunchAction? value)
    {
        if (value is { } action)
        {
            // Never throws for I/O; a failed save keeps the choice for this session.
            _ = preferences.SaveAfterLaunchAsync(action);
        }
    }
}
