using PzLauncher.Desktop.Services.Shell;
using PzLauncher.Desktop.Themes;
using PzLauncher.Desktop.ViewModels.ZombieBuddy;

namespace PzLauncher.Desktop.Tests.TestSupport;

/// <summary>
/// The setup dialog returns <see cref="SetupResult"/>'s answer without running the operation unless the handler starts it.
/// Prompts and confirms take their answers from the queues in order; an empty queue cancels.
/// </summary>
public sealed class FakeDialogService : IDialogService
{
    public List<string> Errors { get; } = [];

    public List<ConfirmPrompt> Confirms { get; } = [];

    public List<TextPrompt> Prompts { get; } = [];

    public Queue<bool> ConfirmAnswers { get; } = new();

    public Queue<TextPromptResult> PromptAnswers { get; } = new();

    public List<(ConfirmPrompt Prompt, string OptionText)> OptionConfirms { get; } = [];

    /// <summary>An empty queue cancels with the option unchecked.</summary>
    public Queue<ConfirmOptionResult> OptionAnswers { get; } = new();

    public List<ZombieBuddySetupViewModel> Setups { get; } = [];

    public Func<ZombieBuddySetupViewModel, Task<bool>> SetupResult { get; set; } = _ => Task.FromResult(true);

    public event Action? Shown;

    public Task ShowErrorAsync(string message)
    {
        Errors.Add(message);
        Shown?.Invoke();
        return Task.CompletedTask;
    }

    public Task<bool> ConfirmAsync(ConfirmPrompt prompt)
    {
        Confirms.Add(prompt);
        Shown?.Invoke();
        return Task.FromResult(ConfirmAnswers.Count > 0 && ConfirmAnswers.Dequeue());
    }

    public Task<ConfirmOptionResult> ConfirmWithOptionAsync(ConfirmPrompt prompt, string optionText)
    {
        OptionConfirms.Add((prompt, optionText));
        Shown?.Invoke();
        return Task.FromResult(OptionAnswers.Count > 0 ? OptionAnswers.Dequeue() : new ConfirmOptionResult(false, false));
    }

    public Task<TextPromptResult> PromptTextAsync(TextPrompt prompt)
    {
        Prompts.Add(prompt);
        Shown?.Invoke();
        return Task.FromResult(PromptAnswers.Count > 0 ? PromptAnswers.Dequeue() : new TextPromptResult.Cancelled());
    }

    public void Accept(string text) => PromptAnswers.Enqueue(new TextPromptResult.Accepted(text));

    public Task<bool> ShowZombieBuddySetupAsync(ZombieBuddySetupViewModel setup)
    {
        Setups.Add(setup);
        return SetupResult(setup);
    }

    /// <summary>Runs the operation as the real dialog does on open, then answers.</summary>
    public static Func<ZombieBuddySetupViewModel, Task<bool>> RunsSetup(bool launch = true) => async setup =>
    {
        await setup.StartAsync();
        return launch;
    };
}

public sealed class FakeClipboardService : IClipboardService
{
    public string? Text { get; set; }

    public Task SetTextAsync(string text)
    {
        Text = text;
        return Task.CompletedTask;
    }

    public Task<string?> GetTextAsync() => Task.FromResult(Text);
}

public sealed class FakeThemeService(AppTheme initial) : IThemeService
{
    public AppTheme Current { get; private set; } = initial;

    public List<AppTheme> Applied { get; } = [];

    public void SetTheme(AppTheme theme)
    {
        Current = theme;
        Applied.Add(theme);
    }
}

public sealed class FakeWindowPlacementService : IWindowPlacementService
{
    public int ResetCalls { get; private set; }

    public void ResetToDefault() => ResetCalls++;
}

public sealed class FakeAppLifetime : IAppLifetime
{
    public int CloseCalls { get; private set; }

    public void Close() => CloseCalls++;
}

public sealed class FakeUrlLauncher : IUrlLauncher
{
    public List<Uri> Opened { get; } = [];

    public Task<bool> OpenAsync(Uri url)
    {
        Opened.Add(url);
        return Task.FromResult(true);
    }
}
