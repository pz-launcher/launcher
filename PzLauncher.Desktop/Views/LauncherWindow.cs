using Avalonia.Controls;

namespace PzLauncher.Desktop.Views;

/// <summary>Minimizes, hides and brings back the main window: after a launch, from the tray, and when the launcher is started again.</summary>
public sealed class LauncherWindow
{
    private readonly Window _window;

    // Restoring to Normal would drop a maximized state, so the last non-minimized state is kept.
    private WindowState _restoreTo;

    private LauncherWindow(Window window)
    {
        _window = window;
        _restoreTo = window.WindowState == WindowState.Minimized ? WindowState.Normal : window.WindowState;
        window.PropertyChanged += (_, e) =>
        {
            if (e.Property == Window.WindowStateProperty && e.NewValue is WindowState state and not WindowState.Minimized)
            {
                _restoreTo = state;
            }
        };
    }

    public bool IsHidden => !_window.IsVisible;

    public static LauncherWindow Attach(Window window) => new(window);

    public void Minimize() => _window.WindowState = WindowState.Minimized;

    public void Hide() => _window.Hide();

    public void Restore()
    {
        if (!_window.IsVisible)
        {
            _window.Show();
        }

        if (_window.WindowState == WindowState.Minimized)
        {
            _window.WindowState = _restoreTo;
        }

        _window.Activate();
    }
}
