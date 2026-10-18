using System;
using System.Threading.Tasks;
using Avalonia.Controls;

namespace PzLauncher.Desktop.Services;

public sealed class AvaloniaUrlLauncher(Func<Window?> owner) : IUrlLauncher
{
    public async Task<bool> OpenAsync(Uri url) =>
        owner() is { } window && await window.Launcher.LaunchUriAsync(url);
}
