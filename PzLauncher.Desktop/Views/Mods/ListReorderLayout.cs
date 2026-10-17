using System;
using System.Collections.Generic;

namespace PzLauncher.Desktop.Views.Mods;

/// <summary>Geometry of a drag reorder over rows stacked from 0 with no spacing, as laid out before the drag.</summary>
public sealed class ListReorderLayout
{
    /// <summary>Pointer travel in DIPs before a press on a handle lifts the row.</summary>
    public const double LiftThreshold = 4;

    /// <summary>Band at the viewport's top and bottom in which a lifted row scrolls the list.</summary>
    public const double AutoScrollEdge = 40;

    /// <summary>Scroll per auto-scroll tick with the pointer on or past the edge.</summary>
    public const double AutoScrollMaxStep = 18;

    private readonly double[] _tops;
    private readonly double[] _heights;

    public ListReorderLayout(IReadOnlyList<double> heights)
    {
        _heights = [.. heights];
        _tops = new double[_heights.Length];
        var top = 0d;
        for (var i = 0; i < _heights.Length; i++)
        {
            _tops[i] = top;
            top += _heights[i];
        }

        TotalHeight = top;
    }

    public int Count => _heights.Length;

    public double TotalHeight { get; }

    public double TopOf(int index) => _tops[index];

    public double HeightOf(int index) => _heights[index];

    public static bool IsLift(double pressY, double y) => Math.Abs(y - pressY) >= LiftThreshold;

    /// <summary>The index the row from <paramref name="from"/> lands at with its middle at <paramref name="center"/>.</summary>
    public int TargetIndex(int from, double center)
    {
        // Compared with the rows' places before the drag, so a row that slid aside doesn't flip back under the pointer.
        var index = 0;
        for (var i = 0; i < _heights.Length; i++)
        {
            if (i != from && _tops[i] + (_heights[i] / 2) < center)
            {
                index++;
            }
        }

        return index;
    }

    /// <summary>How far the row at <paramref name="index"/> slides to open the gap at <paramref name="to"/>.</summary>
    public double ShiftOf(int index, int from, int to)
    {
        if (from < index && index <= to)
        {
            return -_heights[from];
        }

        return to <= index && index < from ? _heights[from] : 0;
    }

    /// <summary>The top of the gap the row from <paramref name="from"/> leaves at <paramref name="to"/>.</summary>
    public double LandingTop(int from, int to) =>
        to <= from ? _tops[to] : _tops[to] + _heights[to] - _heights[from];

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
