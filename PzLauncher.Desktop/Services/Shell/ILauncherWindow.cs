namespace PzLauncher.Desktop.Services.Shell;

/// <summary>The main window around a game session. Used on the UI thread.</summary>
public interface ILauncherWindow
{
    /// <summary>Hidden by <see cref="HideToTray"/> and not brought back since.</summary>
    bool IsHidden { get; }

    void Minimize();

    /// <summary>Hides the window, taskbar entry included; the tray icon brings it back.</summary>
    void HideToTray();

    /// <summary>Shows the window if hidden, returns it to its last non-minimized state (maximized stays maximized) and activates it.</summary>
    void Restore();
}
