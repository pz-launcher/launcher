using System;
using Avalonia.Controls;

namespace PzLauncher.Desktop.Services.Shell;

public sealed class AvaloniaAppLifetime(Func<Window?> mainWindow) : IAppLifetime
{
    public void Close() => mainWindow()?.Close();
}
