using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PzLauncher.Core;
using PzLauncher.Core.Platform;
using PzLauncher.Core.Steam;
using PzLauncher.Desktop.Lang;
using PzLauncher.Desktop.Localization;
using PzLauncher.Desktop.Services.Launch;
using PzLauncher.Desktop.Services.Mods;
using PzLauncher.Desktop.Services.Net;
using PzLauncher.Desktop.Services.Preferences;
using PzLauncher.Desktop.Services.Shell;
using PzLauncher.Desktop.Services.Steam;
using PzLauncher.Desktop.Services.Update;
using PzLauncher.Desktop.Services.ZombieBuddy;
using PzLauncher.Desktop.Themes;
using PzLauncher.Desktop.ViewModels;
using PzLauncher.Desktop.ViewModels.Home;
using PzLauncher.Desktop.ViewModels.Installs;
using PzLauncher.Desktop.ViewModels.Java;
using PzLauncher.Desktop.ViewModels.Mods;
using PzLauncher.Desktop.ViewModels.Net;
using PzLauncher.Desktop.ViewModels.Settings;
using PzLauncher.Desktop.ViewModels.LaunchSettings;
using PzLauncher.Desktop.ViewModels.Update;
using PzLauncher.Desktop.Views;

namespace PzLauncher.Desktop;

public partial class App : Application
{
    private bool _showingErrorDialog;

    /// <summary>Set by <c>Program.Main</c>; the designer has none.</summary>
    public SingleInstanceGuard? Instance { get; init; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var preferencesPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PzLauncher", "preferences.json");

            // Composition root: the only place that builds the container.
            var services = new ServiceCollection();
            services.AddLogging(logging => logging.AddDebug());
            services.AddPzLauncherCore(typeof(App).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);
            services.AddSingleton<IFolderPicker>(new AvaloniaFolderPicker(() => desktop.MainWindow));
            services.AddSingleton<IDialogService>(new AvaloniaDialogService(() => desktop.MainWindow));
            services.AddSingleton<IClipboardService>(new AvaloniaClipboardService(() => desktop.MainWindow));
            services.AddSingleton<IUrlLauncher>(new AvaloniaUrlLauncher(() => desktop.MainWindow));
            services.AddSingleton<IAppLifetime>(new AvaloniaAppLifetime(() => desktop.MainWindow));
            WindowPlacementPersistence? windowPlacement = null;
            services.AddSingleton<IWindowPlacementService>(new WindowPlacementService(() => windowPlacement));
            LauncherWindow? launcherWindow = null;
            services.AddSingleton<ILauncherWindow>(new LauncherWindowService(() => launcherWindow));
            services.AddSingleton<ISecretProtector>(sp => sp.GetRequiredService<IPlatformEnvironment>().Os switch
            {
                PlatformKind.Windows => new WindowsDpapiSecretProtector(),
                PlatformKind.MacOS or PlatformKind.Linux => new PlainSecretProtector(),
            });
            services.AddSingleton<IPreferenceStore>(sp => new JsonPreferenceStore(
                preferencesPath, sp.GetRequiredService<ILogger<JsonPreferenceStore>>(), sp.GetRequiredService<ISecretProtector>()));
            services.AddSingleton(sp => sp.GetRequiredService<IPreferenceStore>().Load());
            services.AddSingleton<IProxySettingsService, ProxySettingsService>();
            services.AddSingleton<IInstallOverridesService, InstallOverridesService>();
            services.AddSingleton<IZombieBuddyChannelService, ZombieBuddyChannelService>();
            services.AddSingleton<IModCollectionDefaultsService, ModCollectionDefaultsService>();
            services.AddSingleton<ModCollectionApplier>();
            services.AddSingleton<IModCollectionOrderWriter, ModCollectionOrderWriter>();
            services.AddSingleton<WorkshopCollectionHistory>();
            services.AddSingleton<WorkshopUserCollectionsCache>();
            services.AddSingleton<WorkshopFavorites>();
            services.AddSingleton<WorkshopTitles>();
            services.AddSingleton<WorkshopLaunchCheck>();
            services.AddSingleton<GameRunState>();
            services.AddSingleton<AfterLaunchSettings>();
            services.AddSingleton<UpdateCoordinator>();
            // The apphost's path; Steam starts the launcher through it.
            var launcherExe = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "PzLauncher.exe");
            services.AddSingleton(sp => new SteamLaunchHookFlow(
                sp.GetRequiredService<ISteamLaunchHookService>(), sp.GetRequiredService<IDialogService>(), launcherExe));
            services.AddSingleton<ILocalizationService>(sp => new LocalizationService(
                sp.GetRequiredService<UserPreferences>().Language ?? AppLanguages.FromCulture(CultureInfo.CurrentUICulture)));
            services.AddSingleton<IThemeService>(sp =>
                new AvaloniaThemeService(this, sp.GetRequiredService<UserPreferences>().Theme ?? AppTheme.Light));
            // Singletons: Home and Installs share one InstallsViewModel, the Java list is shared app-wide, and page state must outlive a MainView rebuild.
            services.AddSingleton<InstallsViewModel>();
            services.AddSingleton<JavaRuntimesViewModel>();
            services.AddSingleton<HomeViewModel>();
            services.AddSingleton<LaunchSettingsViewModel>();
            services.AddSingleton<ModsViewModel>();
            services.AddSingleton<UpdateSettingsViewModel>();
            services.AddSingleton<ProxySettingsViewModel>();
            services.AddSingleton<SteamSettingsViewModel>();
            services.AddSingleton<UpdateBannerViewModel>();
            services.AddSingleton<SettingsViewModel>();
            services.AddSingleton<LanguageMenuViewModel>();
            services.AddSingleton<MainViewModel>();
            var provider = services.BuildServiceProvider();

            InstallGlobalExceptionHandlers(provider.GetRequiredService<ILogger<App>>(), provider.GetRequiredService<IDialogService>());

            // Applies the saved proxy; before anything else is resolved, so no request (the startup update check included) goes out without it.
            var proxy = provider.GetRequiredService<IProxySettingsService>();

            // Resolved before the first window exists, so views read UiText in the startup language and theme.
            var localization = provider.GetRequiredService<ILocalizationService>();
            provider.GetRequiredService<IThemeService>();

            // Posted: the event fires inside the language menu item's click, and rebuilding now would tear that menu down mid-click.
            localization.LanguageChanged += (_, _) => Dispatcher.UIThread.Post(() => RebuildMainView(desktop));

            var mainWindow = new MainWindow
            {
                DataContext = provider.GetRequiredService<MainViewModel>(),
            };
            var installOverrides = provider.GetRequiredService<IInstallOverridesService>();
            var orderWriter = provider.GetRequiredService<IModCollectionOrderWriter>();
            var updates = provider.GetRequiredService<UpdateCoordinator>();
            windowPlacement = WindowPlacementPersistence.Attach(
                mainWindow,
                provider.GetRequiredService<IPreferenceStore>(),
                provider.GetRequiredService<UserPreferences>().Window,
                // A failed order write is already logged by Core; closing goes on without it.
                ct => Task.WhenAll(installOverrides.FlushAsync(ct), orderWriter.FlushAsync(ct), proxy.FlushAsync(ct)),
                // A staged update installs whether or not "Later" was clicked; a failure is logged and offered again next start.
                updates.InstallPendingOnCloseAsync,
                provider.GetRequiredService<ILogger<WindowPlacementPersistence>>());
            desktop.MainWindow = mainWindow;
            var window = LauncherWindow.Attach(mainWindow);
            launcherWindow = window;
            if (Instance is { } instance)
            {
                // Raised on a thread-pool thread.
                instance.ActivationRequested += (_, _) => Dispatcher.UIThread.Post(window.Restore);
            }

            AddTrayIcon(mainWindow, window, provider.GetRequiredService<IAppLifetime>(), localization);

            // Once the window shows, so neither delays the first frame; failures are only logged.
            var steam = provider.GetRequiredService<SteamSettingsViewModel>();
            mainWindow.Opened += (_, _) =>
            {
                _ = updates.StartAsync();
                _ = steam.RemindAtStartupAsync();
            };
            desktop.Exit += (_, _) => provider.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }

    // Shown whatever the after-launch choice: it is how a window hidden after a launch comes back.
    private void AddTrayIcon(Window mainWindow, LauncherWindow window, IAppLifetime lifetime, ILocalizationService localization)
    {
        var show = new NativeMenuItem();
        show.Click += (_, _) => window.Restore();
        var exit = new NativeMenuItem();
        exit.Click += (_, _) => lifetime.Close();
        var tray = new TrayIcon
        {
            Icon = mainWindow.Icon,
            ToolTipText = "PzLauncher",
            Menu = new NativeMenu { Items = { show, exit } },
        };
        tray.Clicked += (_, _) => window.Restore();

        void RenderTexts()
        {
            show.Header = UiText.Tray_Show;
            exit.Header = UiText.Tray_Exit;
        }

        RenderTexts();

        // Never unsubscribed: the tray icon and the localization service live as long as the app.
        localization.LanguageChanged += (_, _) => RenderTexts();
        TrayIcon.SetIcons(this, [tray]);
    }

    // Views read text once via {x:Static}, so a language switch rebuilds the window's content around the same view models.
    private static void RebuildMainView(IClassicDesktopStyleApplicationLifetime desktop)
    {
        if (desktop.MainWindow is { } window)
        {
            window.Content = new MainView();
        }
    }

    // The only place that catches Exception.
    private void InstallGlobalExceptionHandlers(ILogger logger, IDialogService dialogs)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            logger.LogCritical(e.ExceptionObject as Exception, "Unhandled exception (terminating: {IsTerminating})", e.IsTerminating);

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            logger.LogError(e.Exception, "Unobserved task exception");
            e.SetObserved();
        };

        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            logger.LogError(e.Exception, "Unhandled exception on the UI thread");
            e.Handled = true;

            // Fire and forget: a failing dialog becomes a logged unobserved task exception instead of re-entering this handler.
            _ = ShowUnexpectedErrorDialogAsync(dialogs);
        };
    }

    private async Task ShowUnexpectedErrorDialogAsync(IDialogService dialogs)
    {
        if (_showingErrorDialog)
        {
            return;
        }

        _showingErrorDialog = true;
        try
        {
            await dialogs.ShowErrorAsync(UiText.Dialog_UnexpectedError);
        }
        finally
        {
            _showingErrorDialog = false;
        }
    }
}
