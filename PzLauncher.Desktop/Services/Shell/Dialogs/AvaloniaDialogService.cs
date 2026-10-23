using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using PzLauncher.Desktop.Lang;
using PzLauncher.Desktop.ViewModels.Mods.Workshop.Actions;
using PzLauncher.Desktop.ViewModels;
using PzLauncher.Desktop.ViewModels.Mods.Workshop;
using PzLauncher.Desktop.ViewModels.ZombieBuddy;
using PzLauncher.Desktop.Views.Mods.Workshop;
using PzLauncher.Desktop.Views.ZombieBuddy;

namespace PzLauncher.Desktop.Services.Shell.Dialogs;

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

    public async Task<bool> ConfirmAsync(ConfirmPrompt prompt) =>
        (await ShowConfirmAsync(prompt, optionText: null, alternativeText: null)).Choice == ConfirmChoice.Confirm;

    public async Task<ConfirmOptionResult> ConfirmWithOptionAsync(ConfirmPrompt prompt, string optionText)
    {
        var (choice, optionChecked) = await ShowConfirmAsync(prompt, optionText, alternativeText: null);
        return new ConfirmOptionResult(choice == ConfirmChoice.Confirm, optionChecked);
    }

    public async Task<ConfirmChoice> ChooseAsync(ConfirmPrompt prompt, string alternativeText) =>
        (await ShowConfirmAsync(prompt, optionText: null, alternativeText)).Choice;

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

    public Task<bool> ShowZombieBuddySetupAsync(ZombieBuddySetupViewModel setup) =>
        ShowOperationDialogAsync(setup, setup.Title, 480, () => new ZombieBuddySetupView { DataContext = setup });

    public Task ShowWorkshopActionAsync(WorkshopActionViewModel action) =>
        ShowOperationDialogAsync(action, action.Title, 520, () => new WorkshopActionView { DataContext = action });

    public Task<bool> PickWorkshopModsAsync(WorkshopModPickViewModel pick) =>
        ShowViewDialogAsync(pick, UiText.WorkshopCollections_PickTitle, 520, () => new WorkshopModPickView { DataContext = pick });

    public async Task<T?> ChooseOneAsync<T>(ChoicePrompt<T> prompt)
        where T : struct
    {
        if (owner() is not { } window)
        {
            return null;
        }

        var options = prompt.Options
            .Select(option => (option.Value, Button: new RadioButton
            {
                Content = option.Text,
                IsChecked = EqualityComparer<T>.Default.Equals(option.Value, prompt.Preselected),
            }))
            .ToList();
        var ok = new Button { Content = UiText.Dialog_Ok, Classes = { "accent" }, IsDefault = true };
        var content = new StackPanel { Margin = new Thickness(16), Spacing = 12 };
        content.Children.Add(new TextBlock { Text = prompt.Message, TextWrapping = TextWrapping.Wrap });
        var choices = new StackPanel { Spacing = 4 };
        choices.Children.AddRange(options.Select(option => option.Button));
        content.Children.Add(choices);
        content.Children.Add(new TextBlock { Text = prompt.Hint, TextWrapping = TextWrapping.Wrap, Classes = { "hint" } });
        content.Children.Add(Buttons(ok));
        var dialog = CreateDialog(prompt.Title, 460, content);
        T? result = null;
        CloseOnClick(ok, dialog, () => result = options.Where(option => option.Button.IsChecked == true).Select(option => (T?)option.Value).FirstOrDefault());

        // No cancel button, so Esc is wired by hand; it dismisses like the window's close button.
        dialog.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                dialog.Close();
            }
        };
        await dialog.ShowDialog(window);
        return result;
    }

    private Task<bool> ShowOperationDialogAsync(DialogOperationViewModel operation, string title, double width, Func<Control> view) =>
        ShowViewDialogAsync(operation, title, width, view, dialog =>
        {
            dialog.Opened += (_, _) => _ = operation.StartAsync();

            // Covers the window's own close button; after a close request there is nothing left to cancel.
            dialog.Closed += (_, _) => operation.Abort();
        });

    private async Task<bool> ShowViewDialogAsync(DialogViewModel viewModel, string title, double width, Func<Control> view, Action<Window>? setUp = null)
    {
        if (owner() is not { } window)
        {
            return false;
        }

        var dialog = CreateDialog(title, width, view());
        var result = false;
        viewModel.CloseRequested += (_, closeResult) =>
        {
            result = closeResult;
            dialog.Close();
        };
        setUp?.Invoke(dialog);
        await dialog.ShowDialog(window);
        return result;
    }

    // Without option text, no option is shown and OptionChecked is false; without alternative text, no second button.
    private async Task<(ConfirmChoice Choice, bool OptionChecked)> ShowConfirmAsync(ConfirmPrompt prompt, string? optionText, string? alternativeText)
    {
        if (owner() is not { } window)
        {
            return (ConfirmChoice.Cancel, false);
        }

        var confirm = new Button { Content = prompt.ConfirmText, Classes = { "accent" } };
        var alternative = alternativeText is null ? null : new Button { Content = alternativeText };
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

        content.Children.Add(alternative is null ? Buttons(confirm, cancel) : Buttons(confirm, alternative, cancel));
        var dialog = CreateDialog(prompt.Title, 460, content);
        var choice = ConfirmChoice.Cancel;
        CloseOnClick(confirm, dialog, () => choice = ConfirmChoice.Confirm);
        if (alternative is not null)
        {
            CloseOnClick(alternative, dialog, () => choice = ConfirmChoice.Alternative);
        }

        CloseOnClick(cancel, dialog);
        await dialog.ShowDialog(window);
        return (choice, option?.IsChecked == true);
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
