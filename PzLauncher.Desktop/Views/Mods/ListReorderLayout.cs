using System;

namespace PzLauncher.Desktop.Views.Mods;

/// <summary>
/// Geometry of a drag reorder over <paramref name="count"/> rows of <paramref name="rowHeight"/>, stacked with no spacing
/// from <paramref name="listTop"/> in the scrolled content, as laid out before the drag. Viewport positions take the scroll offset.
/// </summary>
public sealed class ListReorderLayout(int count, double rowHeight, double listTop)
{
    /// <summary>Pointer travel in DIPs before a press on a handle lifts the row.</summary>
    public const double LiftThreshold = 4;

    /// <summary>Band at the viewport's top and bottom in which a lifted row scrolls the list.</summary>
    public const double AutoScrollEdge = 40;

    /// <summary>Scroll per auto-scroll tick with the pointer on or past the edge.</summary>
    public const double AutoScrollMaxStep = 18;

    public double TotalHeight => count * rowHeight;

    public double TopOf(int index) => index * rowHeight;

    public double ListTopInViewport(double scrollOffset) => listTop - scrollOffset;

    public static bool IsLift(double pressY, double y) => Math.Abs(y - pressY) >= LiftThreshold;

    /// <summary>The index the row from <paramref name="from"/> lands at with its top at <paramref name="top"/> in the viewport.</summary>
    public int TargetIndex(int from, double top, double scrollOffset)
    {
        // Compared with the rows' places before the drag, so a row that slid aside doesn't flip back under the pointer:
        // the lifted row's middle passes row i's middle exactly when its top passes row i's top.
        var passed = Math.Clamp((int)Math.Ceiling((top - ListTopInViewport(scrollOffset)) / rowHeight), 0, count);
        return from < passed ? passed - 1 : passed;
    }

    /// <summary>How far the row at <paramref name="index"/> slides to open the gap at <paramref name="to"/>.</summary>
    public double ShiftOf(int index, int from, int to)
    {
        if (from < index && index <= to)
        {
            return -rowHeight;
        }

        return to <= index && index < from ? rowHeight : 0;
    }

    /// <summary>The part of the viewport the list covers: the viewport, or less when the list is shorter.</summary>
    public (double Min, double Max) VisibleRange(double scrollOffset, double viewportHeight)
    {
        var top = ListTopInViewport(scrollOffset);
        return (Math.Max(0, top), Math.Min(viewportHeight, top + TotalHeight));
    }

    /// <summary>The lifted row's viewport top for <paramref name="top"/>, kept over the visible part of the list.</summary>
    public double FloatTop(double top, double scrollOffset, double viewportHeight)
    {
        var (min, max) = VisibleRange(scrollOffset, viewportHeight);
        return ClampTop(top, rowHeight, min, max);
    }

    /// <summary>The viewport top of slot <paramref name="to"/>; a slot scrolled out of view is approached up to the list's edge.</summary>
    public double LandingTop(int to, double scrollOffset, double viewportHeight) =>
        FloatTop(ListTopInViewport(scrollOffset) + TopOf(to), scrollOffset, viewportHeight);

    /// <summary>The scroll offset after one auto-scroll tick with the pointer at <paramref name="y"/> in the viewport.</summary>
    public double AutoScrollOffset(double scrollOffset, double y, double viewportHeight) =>
        Math.Clamp(scrollOffset + AutoScrollStep(y, viewportHeight), 0, Math.Max(0, listTop + TotalHeight - viewportHeight));

    /// <summary>Keeps a row of <paramref name="height"/> between <paramref name="min"/> and <paramref name="max"/>; <paramref name="min"/> when it doesn't fit.</summary>
    public static double ClampTop(double top, double height, double min, double max) =>
        max - height <= min ? min : Math.Clamp(top, min, max - height);

    /// <summary>Scroll for one tick with the pointer at <paramref name="y"/> in the viewport; negative scrolls up.</summary>
    public static double AutoScrollStep(double y, double viewportHeight)
    {
        if (y < AutoScrollEdge)
        {
            return -AutoScrollMaxStep * Math.Min(1, (AutoScrollEdge - y) / AutoScrollEdge);
        }

        var bottomEdge = viewportHeight - AutoScrollEdge;
        return y > bottomEdge ? AutoScrollMaxStep * Math.Min(1, (y - bottomEdge) / AutoScrollEdge) : 0;
    }
}
