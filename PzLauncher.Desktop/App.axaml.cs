using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PzLauncher.Core;
using PzLauncher.Desktop.Services;
using PzLauncher.Desktop.ViewModels;
using PzLauncher.Desktop.ViewModels.Installs;
using PzLauncher.Desktop.Views;

namespace PzLauncher.Desktop;

public partial class App : Application
{
    private bool _showingErrorDialog;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Composition root: the only place that builds the container.
            var services = new ServiceCollection();
            services.AddLogging(logging => logging.AddDebug());
            services.AddPzLauncherCore();
            services.AddSingleton<IFolderPicker>(new AvaloniaFolderPicker(() => desktop.MainWindow));
            services.AddTransient<InstallsViewModel>();
            services.AddSingleton<MainViewModel>();
            var provider = services.BuildServiceProvider();

            InstallGlobalExceptionHandlers(provider.GetRequiredService<ILogger<App>>());

            desktop.MainWindow = new MainWindow
            {
                DataContext = provider.GetRequiredService<MainViewModel>(),
            };
            desktop.Exit += (_, _) => provider.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }

    // The only place that catches Exception.
    private void InstallGlobalExceptionHandlers(ILogger logger)
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
            _ = ShowUnexpectedErrorDialogAsync();
        };
    }

    private async Task ShowUnexpectedErrorDialogAsync()
    {
        if (_showingErrorDialog || ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime { MainWindow: { } owner })
        {
            return;
        }

        _showingErrorDialog = true;
        try
        {
            var ok = new Button { Content = "OK", HorizontalAlignment = HorizontalAlignment.Right };
            var dialog = new Window
            {
                Title = "PzLauncher",
                Width = 420,
                SizeToContent = SizeToContent.Height,
                CanResize = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Content = new StackPanel
                {
                    Margin = new Thickness(16),
                    Spacing = 12,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = "Something went wrong. The launcher may not work as expected; try again or restart it.",
                            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                        },
                        ok,
                    },
                },
            };
            ok.Click += (_, _) => dialog.Close();
            await dialog.ShowDialog(owner);
        }
        finally
        {
            _showingErrorDialog = false;
        }
    }
}
