using System;
using PzLauncher.Desktop.Services.Shell;

namespace PzLauncher.Desktop.Views;

/// <summary>Forwards to the main window's placement, which is attached only after the container is built.</summary>
public sealed class WindowPlacementService(Func<WindowPlacementPersistence?> persistence) : IWindowPlacementService
{
    public void ResetToDefault() =>
        (persistence() ?? throw new InvalidOperationException("The window was reset before the main window exists.")).ResetToDefault();
}
