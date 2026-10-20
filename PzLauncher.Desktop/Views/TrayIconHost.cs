using Avalonia;
using Avalonia.Controls;
using PzLauncher.Desktop.Lang;
using PzLauncher.Desktop.Localization;

namespace PzLauncher.Desktop.Views;

/// <summary>The tray icon: shown whatever the after-launch choice, since it is how a window hidden after a launch comes back.</summary>
public static class TrayIconHost
{
    public static void Attach(Application app, WindowIcon? icon, LauncherWindow window, ILocalizationService localization)
    {
        var show = new NativeMenuItem();
        show.Click += (_, _) => window.Restore();
        var exit = new NativeMenuItem();
        exit.Click += (_, _) => window.Close();
        var tray = new TrayIcon
        {
            Icon = icon,
            ToolTipText = "PzLauncher",
            Menu = new NativeMenu { Items = { show, exit } },
        };
        tray.Clicked += (_, _) => window.Restore();

        // Set in code, not {x:Static}: the menu outlives the view rebuild a language switch does.
        void RenderTexts()
        {
            show.Header = UiText.Tray_Show;
            exit.Header = UiText.Tray_Exit;
        }

        RenderTexts();

        // Never unsubscribed: the tray icon and the localization service live as long as the app.
        localization.LanguageChanged += (_, _) => RenderTexts();
        TrayIcon.SetIcons(app, [tray]);
    }
}
