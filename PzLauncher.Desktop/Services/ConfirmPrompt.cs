using System.Collections.Generic;

namespace PzLauncher.Desktop.Services;

/// <param name="Items">Listed under the message, one per line; none when empty.</param>
public sealed record ConfirmPrompt(string Title, string Message, string ConfirmText, string CancelText)
{
    public IReadOnlyList<string> Items { get; init; } = [];
}
