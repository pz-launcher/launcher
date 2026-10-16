using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using PzLauncher.Desktop.Lang;

namespace PzLauncher.Desktop.Services;

public sealed class AvaloniaDialogService(Func<Window?> owner) : IDialogService
{
    public async Task ShowErrorAsync(string message)
    {
        if (owner() is not { } window)
        {
            return;
        }

        var ok = new Button { Content = UiText.Dialog_Ok, HorizontalAlignment = HorizontalAlignment.Right };
        var dialog = new Window
        {
            Title = "PzLauncher",
            FontFamily = FontFamily.Parse(UiText.Main_FontFamily),
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
                    new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                    ok,
                },
            },
        };
        ok.Click += (_, _) => dialog.Close();
        await dialog.ShowDialog(window);
    }
}
