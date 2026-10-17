using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input.Platform;

namespace PzLauncher.Desktop.Services;

public sealed class AvaloniaClipboardService(Func<Window?> owner) : IClipboardService
{
    public async Task SetTextAsync(string text)
    {
        if (owner()?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(text);
        }
    }

    public async Task<string?> GetTextAsync() =>
        owner()?.Clipboard is { } clipboard ? await clipboard.TryGetTextAsync() : null;
}
