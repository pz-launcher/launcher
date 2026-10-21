using System;
using Avalonia.Controls;
using PzLauncher.Desktop.Services.Shell;

namespace PzLauncher.Desktop.Views;

/// <summary>Minimizes, hides, brings back and closes the main window: after a launch, from the tray, and when the launcher is started again.</summary>
public sealed class LauncherWindow : ILauncherWindow, IAppLifetime
{
    private Window? _window;

    // Restoring to Normal would drop a maximized state, so the last non-minimized state is kept.
    private WindowState _restoreTo;

    public bool IsHidden => !Attached.IsVisible;

    // Null until Attach: the container, and so the main window's view models, are built before the window.
    private Window Attached => _window ?? throw new InvalidOperationException("The launcher window was used before the main window was attached.");

    /// <summary>Call once, with the main window.</summary>
    public void Attach(Window window)
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

    public void Minimize() => Attached.WindowState = WindowState.Minimized;

    public void Hide() => Attached.Hide();

    public void Restore()
    {
        if (!Attached.IsVisible)
        {
            Attached.Show();
        }

        if (Attached.WindowState == WindowState.Minimized)
        {
            Attached.WindowState = _restoreTo;
        }

        Attached.Activate();
    }

    public void Close() => Attached.Close();

    /// <summary>An open dialog's caller would carry on while the app shuts down, so the dialog is brought forward instead.</summary>
    public void CloseUnlessDialogOpen()
    {
        if (Attached.OwnedWindows is [.., var dialog])
        {
            Restore();
            dialog.Activate();
            return;
        }

        Close();
    }
}
