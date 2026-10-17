using System;

namespace PzLauncher.Desktop.Services;

/// <param name="Validate">The problem with the entered text, shown under the box with accept disabled; <c>null</c> when it is acceptable.</param>
/// <param name="SkipText">A third button between accept and cancel; none when <c>null</c>.</param>
public sealed record TextPrompt(
    string Title,
    string Message,
    string InitialText,
    string AcceptText,
    Func<string, string?> Validate,
    string? SkipText = null,
    bool IsMultiline = false);

public abstract record TextPromptResult
{
    private TextPromptResult()
    {
    }

    public sealed record Accepted(string Text) : TextPromptResult;

    public sealed record Skipped : TextPromptResult;

    /// <summary>Cancel, or the window was closed.</summary>
    public sealed record Cancelled : TextPromptResult;
}
