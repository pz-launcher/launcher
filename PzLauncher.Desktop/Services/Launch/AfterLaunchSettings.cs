using CommunityToolkit.Mvvm.ComponentModel;
using PzLauncher.Desktop.Services.Preferences;

namespace PzLauncher.Desktop.Services.Launch;

/// <summary>App-lifetime: the saved <see cref="AfterLaunchAction"/>, shared by Home's launch and the Settings "General" section.</summary>
public sealed partial class AfterLaunchSettings(IPreferenceStore preferences, UserPreferences saved) : ObservableObject
{
    /// <summary><c>null</c> until the user has chosen; setting a choice saves it.</summary>
    [ObservableProperty] public partial AfterLaunchAction? Action { get; set; } = saved.AfterLaunch;

    partial void OnActionChanged(AfterLaunchAction? value)
    {
        if (value is { } action)
        {
            // Never throws for I/O; a failed save keeps the choice for this session.
            _ = preferences.SaveAfterLaunchAsync(action);
        }
    }
}
