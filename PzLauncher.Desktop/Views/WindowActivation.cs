using Avalonia.Controls;
using Avalonia.Threading;
using PzLauncher.Desktop.Services.Shell;

namespace PzLauncher.Desktop.Views;

/// <summary>Brings the main window to the front when the launcher is started again, restoring it if minimised.</summary>
public static class WindowActivation
{
    public static void Attach(Window window, SingleInstanceGuard guard)
    {
        // Restoring to Normal would drop a maximized state, so the last non-minimised state is kept.
        var restoreTo = window.WindowState == WindowState.Minimized ? WindowState.Normal : window.WindowState;
        window.PropertyChanged += (_, e) =>
        {
            if (e.Property == Window.WindowStateProperty && e.NewValue is WindowState state and not WindowState.Minimized)
            {
                restoreTo = state;
            }
        };
        guard.ActivationRequested += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            if (window.WindowState == WindowState.Minimized)
            {
                window.WindowState = restoreTo;
            }

            window.Activate();
        });
    }
}
