using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using FluentIcons.Common;
using PzLauncher.Desktop.Lang;

namespace PzLauncher.Desktop.Views;

public partial class MainView : UserControl
{
    private Window? _window;

    public MainView()
    {
        InitializeComponent();

        // Inter has no CJK glyphs; without named CJK fallbacks, bold Chinese renders at regular weight.
        FontFamily = FontFamily.Parse(UiText.Main_FontFamily);
    }

    // A language switch replaces this view inside a window that may already be maximized, so state is read on attach.
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _window = TopLevel.GetTopLevel(this) as Window;
        if (_window is not null)
        {
            _window.PropertyChanged += OnWindowPropertyChanged;
        }

        UpdateMaximizeButton();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_window is not null)
        {
            _window.PropertyChanged -= OnWindowPropertyChanged;
            _window = null;
        }

        base.OnDetachedFromVisualTree(e);
    }

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Window.WindowStateProperty)
        {
            UpdateMaximizeButton();
        }
    }

    private void UpdateMaximizeButton()
    {
        var maximized = _window?.WindowState == WindowState.Maximized;
        var text = maximized ? UiText.Shell_Restore : UiText.Shell_Maximize;
        MaximizeIcon.Icon = maximized ? Icon.SquareMultiple : Icon.Maximize;
        ToolTip.SetTip(MaximizeButton, text);
        AutomationProperties.SetName(MaximizeButton, text);
    }

    private void OnMinimizeClick(object? sender, RoutedEventArgs e)
    {
        if (_window is not null)
        {
            _window.WindowState = WindowState.Minimized;
        }
    }

    private void OnMaximizeClick(object? sender, RoutedEventArgs e)
    {
        if (_window is not null)
        {
            _window.WindowState = _window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => _window?.Close();
}
