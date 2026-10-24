using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using PzLauncher.Desktop.Services.Shell.Lifetime;
using PzLauncher.Desktop.Services.Shell;

namespace PzLauncher.Desktop.Views;

public sealed record ScreenArea(PixelRect WorkingArea, double Scaling, bool IsPrimary);

/// <summary>A <c>null</c> position leaves the window's own startup location in charge.</summary>
public sealed record WindowStartup(PixelPoint? Position, Size Size, bool IsMaximized);

/// <summary>Position in physical pixels, size in DIPs, as <c>Window.Position</c> and <c>Window.Width</c>/<c>Height</c> take them.</summary>
public sealed record WindowBounds(PixelPoint Position, Size Size);

public static class WindowPlacementRules
{
    // MainView's title-bar row: the window can only be dragged back if enough of it is on a screen.
    private const double TitleBarHeight = 44;
    private const double MinVisibleWidth = 100;
    private const double MinVisibleHeight = 20;

    public static WindowStartup Resolve(WindowPlacement saved, IReadOnlyList<ScreenArea> screens, Size minSize)
    {
        var size = new Size(saved.Width, saved.Height);
        if (ScreenShowingTitleBar(saved, screens, minSize) is { } screen)
        {
            return new WindowStartup(new PixelPoint(saved.X, saved.Y), Clamp(size, minSize, screen), saved.IsMaximized);
        }

        if ((screens.FirstOrDefault(s => s.IsPrimary) ?? screens.FirstOrDefault()) is not { } primary)
        {
            return new WindowStartup(null, Clamp(size, minSize, screen: null), saved.IsMaximized);
        }

        // The frame is unknown before the window is shown, so the client area lands right of center by any left border.
        var centered = Center(size, primary, minSize, clientOffset: default);
        return new WindowStartup(centered.Position, centered.Size, saved.IsMaximized);
    }

    /// <summary>The clamped size, positioned so the client area (<paramref name="clientOffset"/> from the window position) is in the middle of the working area.</summary>
    public static WindowBounds Center(Size size, ScreenArea screen, Size minSize, PixelVector clientOffset)
    {
        size = Clamp(size, minSize, screen);
        var area = screen.WorkingArea;
        var width = (int)Math.Round(size.Width * screen.Scaling);
        var height = (int)Math.Round(size.Height * screen.Scaling);
        var client = new PixelPoint(area.X + Math.Max(0, (area.Width - width) / 2), area.Y + Math.Max(0, (area.Height - height) / 2));
        return new WindowBounds(client - clientOffset, size);
    }

    private static ScreenArea? ScreenShowingTitleBar(WindowPlacement saved, IReadOnlyList<ScreenArea> screens, Size minSize)
    {
        ScreenArea? best = null;
        var bestArea = 0d;
        foreach (var screen in screens)
        {
            var area = screen.WorkingArea;
            // Doubles: a hand-edited position near int.MaxValue would overflow PixelRect arithmetic.
            // Clamped width: an oversized window shrinks towards its left edge, which can leave the title bar off-screen.
            var width = Overlap(saved.X, Clamp(new Size(saved.Width, saved.Height), minSize, screen).Width * screen.Scaling, area.X, area.Width);
            var height = Overlap(saved.Y, TitleBarHeight * screen.Scaling, area.Y, area.Height);
            if (width >= MinVisibleWidth * screen.Scaling && height >= MinVisibleHeight * screen.Scaling && width * height > bestArea)
            {
                best = screen;
                bestArea = width * height;
            }
        }

        return best;
    }

    private static double Overlap(double start, double length, double otherStart, double otherLength) =>
        Math.Max(0, Math.Min(start + length, otherStart + otherLength) - Math.Max(start, otherStart));

    private static Size Clamp(Size size, Size minSize, ScreenArea? screen)
    {
        var width = size.Width;
        var height = size.Height;
        if (screen is not null)
        {
            width = Math.Min(width, screen.WorkingArea.Width / screen.Scaling);
            height = Math.Min(height, screen.WorkingArea.Height / screen.Scaling);
        }

        return new Size(Math.Max(width, minSize.Width), Math.Max(height, minSize.Height));
    }
}
