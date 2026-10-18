namespace PzLauncher.Desktop.Services.Shell;

/// <summary>The main window's normal (restored) bounds: position in physical screen pixels, client size in DIPs.</summary>
public sealed record WindowPlacement(int X, int Y, double Width, double Height, bool IsMaximized);
