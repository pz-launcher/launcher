namespace PzLauncher.Desktop.Services.Launch;

/// <summary>What the launcher window does once the game has started. Declaration order is display order.</summary>
public enum AfterLaunchAction
{
    Keep,
    Minimize,
    MinimizeToTray,
    Close,
}
