using Avalonia;
using System;
using System.Linq;
using PzLauncher.Core.Update;
using PzLauncher.Desktop.Services.Shell;

namespace PzLauncher.Desktop;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // Disposed on this thread after the app exits: the mutex belongs to the thread that took it.
        using var instance = SingleInstanceGuard.Acquire(AppContext.BaseDirectory, args.Contains(LauncherArguments.Restart));
        if (instance is null)
        {
            return;
        }

        BuildAvaloniaApp(() => new App { Instance = instance }).StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp() => BuildAvaloniaApp(() => new App());

    private static AppBuilder BuildAvaloniaApp(Func<App> createApp)
        => AppBuilder.Configure(createApp)
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}