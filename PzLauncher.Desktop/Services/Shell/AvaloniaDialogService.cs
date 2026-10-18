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
        CloseOnClick(ok, dialog);
        await dialog.ShowDialog(window);
    }

    public async Task<bool> ConfirmAsync(ConfirmPrompt prompt) => (await ShowConfirmAsync(prompt, optionText: null)).Confirmed;

    public Task<ConfirmOptionResult> ConfirmWithOptionAsync(ConfirmPrompt prompt, string optionText) =>
        ShowConfirmAsync(prompt, optionText);

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
        CloseOnClick(accept, dialog, () => result = new TextPromptResult.Accepted(input.Text ?? ""));
        if (buttons.Length == 3)
        {
            CloseOnClick(buttons[1], dialog, () => result = new TextPromptResult.Skipped());
        }

        CloseOnClick(cancel, dialog);
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

    // Without option text, no option is shown and OptionChecked is false.
    private async Task<ConfirmOptionResult> ShowConfirmAsync(ConfirmPrompt prompt, string? optionText)
    {
        if (owner() is not { } window)
        {
            return new ConfirmOptionResult(false, false);
        }

        var confirm = new Button { Content = prompt.ConfirmText, Classes = { "accent" } };
        var cancel = new Button { Content = prompt.CancelText, IsCancel = true };
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

        var option = optionText is null ? null : new CheckBox { Content = optionText };
        if (option is not null)
        {
            content.Children.Add(option);
        }

        content.Children.Add(Buttons(confirm, cancel));
        var dialog = CreateDialog(prompt.Title, 460, content);
        var confirmed = false;
        CloseOnClick(confirm, dialog, () => confirmed = true);
        CloseOnClick(cancel, dialog);
        await dialog.ShowDialog(window);
        return new ConfirmOptionResult(confirmed, option?.IsChecked == true);
    }

    private static void CloseOnClick(Button button, Window dialog, Action? setResult = null) =>
        button.Click += (_, _) =>
        {
            setResult?.Invoke();
            dialog.Close();
        };

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
