using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using PzLauncher.Desktop.Lang;
using PzLauncher.Desktop.ViewModels.ZombieBuddy;
using PzLauncher.Desktop.Views.ZombieBuddy;

namespace PzLauncher.Desktop.Services.Shell;

public sealed class AvaloniaDialogService(Func<Window?> owner) : IDialogService
{
    public async Task ShowErrorAsync(string message)
    {
        if (owner() is not { } window)
        {
            return;
        }

        var ok = new Button { Content = UiText.Dialog_Ok, HorizontalAlignment = HorizontalAlignment.Right };
        var dialog = CreateDialog("PzLauncher", 420, new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                ok,
            },
        });
        ok.Click += (_, _) => dialog.Close();
        await dialog.ShowDialog(window);
    }

    public async Task<bool> ConfirmAsync(ConfirmPrompt prompt)
    {
        if (owner() is not { } window)
        {
            return false;
        }

        var confirm = new Button { Content = prompt.ConfirmText, Classes = { "accent" } };
        var cancel = new Button { Content = prompt.CancelText, IsCancel = true };
        var content = MessageContent(prompt);
        content.Children.Add(Buttons(confirm, cancel));
        var dialog = CreateDialog(prompt.Title, 460, content);
        var confirmed = false;
        confirm.Click += (_, _) =>
        {
            confirmed = true;
            dialog.Close();
        };
        cancel.Click += (_, _) => dialog.Close();
        await dialog.ShowDialog(window);
        return confirmed;
    }

    public async Task<ConfirmOptionResult> ConfirmWithOptionAsync(ConfirmPrompt prompt, string optionText)
    {
        if (owner() is not { } window)
        {
            return new ConfirmOptionResult(false, false);
        }

        var confirm = new Button { Content = prompt.ConfirmText, Classes = { "accent" } };
        var cancel = new Button { Content = prompt.CancelText, IsCancel = true };
        var option = new CheckBox { Content = optionText };
        var content = MessageContent(prompt);
        content.Children.Add(option);
        content.Children.Add(Buttons(confirm, cancel));
        var dialog = CreateDialog(prompt.Title, 460, content);
        var confirmed = false;
        confirm.Click += (_, _) =>
        {
            confirmed = true;
            dialog.Close();
        };
        cancel.Click += (_, _) => dialog.Close();
        await dialog.ShowDialog(window);
        return new ConfirmOptionResult(confirmed, option.IsChecked == true);
    }

    public async Task<TextPromptResult> PromptTextAsync(TextPrompt prompt)
    {
        if (owner() is not { } window)
        {
            return new TextPromptResult.Cancelled();
        }

        var input = new TextBox
        {
            Text = prompt.InitialText,
            AcceptsReturn = prompt.IsMultiline,
            TextWrapping = prompt.IsMultiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
            Height = prompt.IsMultiline ? 160 : double.NaN,
        };
        // Looked up once: the theme can't change while a modal dialog is open.
        var problem = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = window.TryFindResource("Shell.ErrorForeground", window.ActualThemeVariant, out var brush) ? brush as IBrush : null,
        };
        var accept = new Button { Content = prompt.AcceptText, Classes = { "accent" }, IsDefault = !prompt.IsMultiline };
        var cancel = new Button { Content = UiText.Dialog_Cancel, IsCancel = true };
        Button[] buttons = prompt.SkipText is { } skipText ? [accept, new Button { Content = skipText }, cancel] : [accept, cancel];

        void Revalidate()
        {
            var message = prompt.Validate(input.Text ?? "");
            problem.Text = message;
            problem.IsVisible = message is not null;
            accept.IsEnabled = message is null;
        }

        input.TextChanged += (_, _) => Revalidate();
        Revalidate();

        var dialog = CreateDialog(prompt.Title, 460, new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = prompt.Message, TextWrapping = TextWrapping.Wrap },
                input,
                problem,
                Buttons(buttons),
            },
        });
        TextPromptResult result = new TextPromptResult.Cancelled();
        accept.Click += (_, _) =>
        {
            result = new TextPromptResult.Accepted(input.Text ?? "");
            dialog.Close();
        };
        if (buttons.Length == 3)
        {
            buttons[1].Click += (_, _) =>
            {
                result = new TextPromptResult.Skipped();
                dialog.Close();
            };
        }

        cancel.Click += (_, _) => dialog.Close();
        dialog.Opened += (_, _) =>
        {
            input.Focus();
            input.SelectAll();
        };
        await dialog.ShowDialog(window);
        return result;
    }

    public async Task<bool> ShowZombieBuddySetupAsync(ZombieBuddySetupViewModel setup)
    {
        if (owner() is not { } window)
        {
            return false;
        }

        var dialog = CreateDialog(setup.Title, 480, new ZombieBuddySetupView { DataContext = setup });
        var launch = false;
        setup.CloseRequested += (_, launchGame) =>
        {
            launch = launchGame;
            dialog.Close();
        };
        dialog.Opened += (_, _) => _ = setup.StartAsync();

        // Covers the window's own close button; after a launch or close request there is nothing left to cancel.
        dialog.Closed += (_, _) => setup.Abort();
        await dialog.ShowDialog(window);
        return launch;
    }

    private static StackPanel MessageContent(ConfirmPrompt prompt)
    {
        var content = new StackPanel { Margin = new Thickness(16), Spacing = 12 };
        content.Children.Add(new TextBlock { Text = prompt.Message, TextWrapping = TextWrapping.Wrap });
        if (prompt.Items.Count > 0)
        {
            content.Children.Add(new ScrollViewer
            {
                MaxHeight = 280,
                Content = new SelectableTextBlock { Text = string.Join(Environment.NewLine, prompt.Items), TextWrapping = TextWrapping.Wrap },
            });
        }

        return content;
    }

    private static Window CreateDialog(string title, double width, Control content) => new()
    {
        Title = title,
        FontFamily = FontFamily.Parse(UiText.Main_FontFamily),
        Width = width,
        SizeToContent = SizeToContent.Height,
        CanResize = false,
        WindowStartupLocation = WindowStartupLocation.CenterOwner,
        Content = content,
    };

    private static StackPanel Buttons(params Button[] buttons)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        panel.Children.AddRange(buttons);
        return panel;
    }
}
