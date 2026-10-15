using System;
using System.Linq;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using PzLauncher.Desktop.Services;

namespace PzLauncher.Desktop.Views;

/// <summary>Restores the window's saved placement before it is shown and saves it when it closes.</summary>
public sealed partial class WindowPlacementPersistence
{
    private static readonly TimeSpan SaveTimeout = TimeSpan.FromSeconds(2);

    private readonly Window _window;
    private readonly IPreferenceStore _store;
    private readonly ILogger _logger;
    private readonly Size _defaultSize;

    private PixelPoint _normalPosition;
    private Size _normalSize;
    private bool _maximized;
    private bool _capturePending;
    private bool _saving;
    private bool _saved;

    private WindowPlacementPersistence(Window window, IPreferenceStore store, WindowPlacement? saved, ILogger logger)
    {
        _window = window;
        _store = store;
        _logger = logger;
        _defaultSize = new Size(window.Width, window.Height);

        var startup = saved is null ? null : Apply(saved);
        _normalPosition = startup?.Position ?? window.Position;
        _normalSize = new Size(window.Width, window.Height);
        _maximized = window.WindowState == WindowState.Maximized;

        window.PositionChanged += (_, _) => ScheduleCapture();
        window.Resized += (_, _) => ScheduleCapture();
        window.PropertyChanged += OnPropertyChanged;
        window.Closing += OnClosing;
    }

    /// <summary>Call before the window is shown.</summary>
    public static WindowPlacementPersistence Attach(Window window, IPreferenceStore store, WindowPlacement? saved, ILogger logger)
    {
        return new WindowPlacementPersistence(window, store, saved, logger);
    }

    /// <summary>The XAML default size, centered on the screen the window is on.</summary>
    public void ResetToDefault()
    {
        // Normal first: a maximized window ignores the size and position, and the tracking only records normal bounds.
        _window.WindowState = WindowState.Normal;

        if ((_window.Screens.ScreenFromWindow(_window) ?? _window.Screens.Primary) is not { } screen)
        {
            _window.Width = _defaultSize.Width;
            _window.Height = _defaultSize.Height;
            return;
        }

        // Windows' invisible left resize border lies between the window position and the client area.
        var client = _window.PointToScreen(default);
        var clientOffset = new PixelVector(client.X - _window.Position.X, client.Y - _window.Position.Y);
        var bounds = WindowPlacementRules.Center(_defaultSize, ToScreenArea(screen), MinSize, clientOffset);
        _window.Width = bounds.Size.Width;
        _window.Height = bounds.Size.Height;
        _window.Position = bounds.Position;
    }

    private Size MinSize => new(_window.MinWidth, _window.MinHeight);

    private static ScreenArea ToScreenArea(Screen screen) => new(screen.WorkingArea, screen.Scaling, screen.IsPrimary);

    private WindowStartup Apply(WindowPlacement saved)
    {
        var screens = _window.Screens.All.Select(ToScreenArea).ToList();
        var startup = WindowPlacementRules.Resolve(saved, screens, MinSize);

        _window.Width = startup.Size.Width;
        _window.Height = startup.Size.Height;
        if (startup.Position is { } position)
        {
            _window.WindowStartupLocation = WindowStartupLocation.Manual;
            _window.Position = position;
        }

        // Set while hidden, so the window opens maximized and restores to the bounds above.
        if (startup.IsMaximized)
        {
            _window.WindowState = WindowState.Maximized;
        }

        return startup;
    }

    // Posted: Win32 reports the move and resize of a maximize or minimize before the new WindowState, so reading
    // the bounds right away would record the maximized or minimized ones.
    private void ScheduleCapture()
    {
        if (_capturePending)
        {
            return;
        }

        _capturePending = true;
        Dispatcher.UIThread.Post(() =>
        {
            _capturePending = false;
            CaptureNormalBounds();
        });
    }

    private void CaptureNormalBounds()
    {
        if (_window.WindowState == WindowState.Normal)
        {
            _normalPosition = _window.Position;
            _normalSize = _window.ClientSize;
        }
    }

    private void OnPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != Window.WindowStateProperty)
        {
            return;
        }

        switch (_window.WindowState)
        {
            case WindowState.Normal:
                _maximized = false;
                break;
            case WindowState.Maximized:
                _maximized = true;
                break;
            // Closing from here reopens in the state the window had before.
            case WindowState.Minimized:
            case WindowState.FullScreen:
                break;
        }
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_saved)
        {
            return;
        }

        CaptureNormalBounds();
        var placement = new WindowPlacement(_normalPosition.X, _normalPosition.Y, _normalSize.Width, _normalSize.Height, _maximized);

        // Holding up an OS shutdown or logoff is worse than losing the placement.
        if (e.CloseReason == WindowCloseReason.OSShutdown)
        {
            _ = _store.SaveWindowPlacementAsync(placement);
            return;
        }

        // A fire-and-forget save at exit can be cut off when the process ends, so the close waits for it.
        e.Cancel = true;
        if (_saving)
        {
            return;
        }

        _saving = true;
        try
        {
            using var timeout = new CancellationTokenSource(SaveTimeout);
            await _store.SaveWindowPlacementAsync(placement, timeout.Token);
        }
        catch (OperationCanceledException)
        {
            LogSaveTimedOut(_logger);
        }
        finally
        {
            _saved = true;
            // Posted: a save that completes synchronously would otherwise close the window from inside this handler,
            // whose Cancel then still applies to the outer close.
            Dispatcher.UIThread.Post(_window.Close);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Saving the window placement timed out; closing without it")]
    private static partial void LogSaveTimedOut(ILogger logger);
}
