using System;
using PzLauncher.Desktop.Services.Shell;

namespace PzLauncher.Desktop.Views;

/// <summary>Forwards to the main window, which is attached only after the container is built.</summary>
public sealed class LauncherWindowService(Func<LauncherWindow?> window) : ILauncherWindow
{
    public bool IsHidden => Window.IsHidden;

    private LauncherWindow Window => window() ?? throw new InvalidOperationException("The launcher window was used before the main window exists.");

    public void Minimize() => Window.Minimize();

    public void HideToTray() => Window.Hide();

    public void Restore() => Window.Restore();
}
