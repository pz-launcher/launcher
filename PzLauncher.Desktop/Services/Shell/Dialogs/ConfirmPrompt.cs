using System.Collections.Generic;

namespace PzLauncher.Desktop.Services.Shell.Dialogs;

/// <param name="Items">Listed under the message, one per line; none when empty.</param>
public sealed record ConfirmPrompt(string Title, string Message, string ConfirmText, string CancelText)
{
    public IReadOnlyList<string> Items { get; init; } = [];
}

/// <param name="Confirmed"><c>false</c> for cancel and for closing the window.</param>
public sealed record ConfirmOptionResult(bool Confirmed, bool OptionChecked);

public enum ConfirmChoice
{
    Confirm,

    /// <summary>The second button, between confirm and cancel.</summary>
    Alternative,

    /// <summary>Also closing the window.</summary>
    Cancel,
}
