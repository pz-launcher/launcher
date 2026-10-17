using PzLauncher.Desktop.Views.Mods;

namespace PzLauncher.Desktop.Tests.Views.Mods;

public sealed class ListReorderLayoutTests
{
    private static readonly ListReorderLayout Four = new(count: 4, rowHeight: 10, listTop: 0);

    // 200 high below a 5 DIP header, in a 100 DIP viewport.
    private static readonly ListReorderLayout Long = new(count: 20, rowHeight: 10, listTop: 5);

    [Theory]
    [InlineData(100, 100, false)]
    [InlineData(100, 103.9, false)]
    [InlineData(100, 104, true)]
    [InlineData(100, 96, true)]
    public void IsLift_NeedsTheThresholdInEitherDirection(double pressY, double y, bool expected) =>
        Assert.Equal(expected, ListReorderLayout.IsLift(pressY, y));

    [Fact]
    public void Layout_StacksRowsFromZero()
    {
        Assert.Equal([0d, 10d, 20d, 30d], Enumerable.Range(0, 4).Select(Four.TopOf));
        Assert.Equal(40, Four.TotalHeight);
    }

    [Theory]
    [InlineData(10, 1)]
    [InlineData(19.9, 1)]
    [InlineData(20.1, 2)]
    [InlineData(30.1, 3)]
    [InlineData(500, 3)]
    [InlineData(0.1, 1)]
    [InlineData(-0.1, 0)]
    [InlineData(-50, 0)]
    public void TargetIndex_PassesARowWhenTheDraggedMiddleCrossesItsMiddle(double top, int expected) =>
        Assert.Equal(expected, Four.TargetIndex(from: 1, top, scrollOffset: 0));

    [Fact]
    public void TargetIndex_ReachesTheTopAndTheBottom()
    {
        Assert.Equal(0, Four.TargetIndex(from: 3, top: -100, scrollOffset: 0));
        Assert.Equal(3, Four.TargetIndex(from: 0, top: 100, scrollOffset: 0));
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(100, 0, 9)]
    [InlineData(150, 0, 14)]
    [InlineData(105, 95, 19)]
    public void TargetIndex_CountsTheScrolledOffRows(double scrollOffset, double top, int expected) =>
        Assert.Equal(expected, Long.TargetIndex(from: 2, top, scrollOffset));

    [Fact]
    public void ShiftOf_DraggingDown_SlidesTheRowsInBetweenUp()
    {
        Assert.Equal([0d, 0d, -10d, -10d], Enumerable.Range(0, 4).Select(i => Four.ShiftOf(i, from: 1, to: 3)));
    }

    [Fact]
    public void ShiftOf_DraggingUp_SlidesTheRowsInBetweenDown()
    {
        Assert.Equal([0d, 10d, 10d, 0d], Enumerable.Range(0, 4).Select(i => Four.ShiftOf(i, from: 3, to: 1)));
        Assert.Equal([10d, 10d, 10d, 0d], Enumerable.Range(0, 4).Select(i => Four.ShiftOf(i, from: 3, to: 0)));
    }

    [Fact]
    public void ShiftOf_InPlace_SlidesNothing()
    {
        Assert.All(Enumerable.Range(0, 4), i => Assert.Equal(0, Four.ShiftOf(i, from: 2, to: 2)));
    }

    [Theory]
    [InlineData(3, 0, 35)]
    [InlineData(0, 0, 5)]
    [InlineData(3, 20, 15)]
    public void LandingTop_IsTheSlotInTheViewport(int to, double scrollOffset, double expected) =>
        Assert.Equal(expected, Long.LandingTop(to, scrollOffset, viewportHeight: 100));

    [Theory]
    [InlineData(3, 100, 0)]
    [InlineData(19, 0, 90)]
    public void LandingTop_SlotOutOfView_StopsAtTheViewportEdge(int to, double scrollOffset, double expected) =>
        Assert.Equal(expected, Long.LandingTop(to, scrollOffset, viewportHeight: 100));

    [Theory]
    [InlineData(50, 0, 50)]
    [InlineData(-20, 0, 5)]
    [InlineData(-20, 100, 0)]
    [InlineData(95, 100, 90)]
    public void FloatTop_StaysOverTheVisibleList(double top, double scrollOffset, double expected) =>
        Assert.Equal(expected, Long.FloatTop(top, scrollOffset, viewportHeight: 100));

    [Fact]
    public void FloatTop_ShortList_StaysOverTheRows() =>
        Assert.Equal(30, Four.FloatTop(90, scrollOffset: 0, viewportHeight: 100));

    [Theory]
    [InlineData(50, 50, 50)]
    [InlineData(0, 0, 0)]
    [InlineData(50, 0, 50 - ListReorderLayout.AutoScrollMaxStep)]
    [InlineData(50, 100, 50 + ListReorderLayout.AutoScrollMaxStep)]
    [InlineData(100, 100, 105)]
    public void AutoScrollOffset_StopsAtTheListsEnds(double scrollOffset, double y, double expected) =>
        Assert.Equal(expected, Long.AutoScrollOffset(scrollOffset, y, viewportHeight: 100), 6);

    [Fact]
    public void AutoScrollOffset_ShortList_NeverScrolls() =>
        Assert.Equal(0, Four.AutoScrollOffset(0, y: 100, viewportHeight: 100));

    [Theory]
    [InlineData(50, 50)]
    [InlineData(-5, 0)]
    [InlineData(95, 80)]
    public void ClampTop_KeepsTheRowInsideTheRange(double top, double expected) =>
        Assert.Equal(expected, ListReorderLayout.ClampTop(top, height: 20, min: 0, max: 100));

    [Fact]
    public void ClampTop_RowTallerThanTheRange_StaysAtTheTop() =>
        Assert.Equal(10, ListReorderLayout.ClampTop(40, height: 50, min: 10, max: 40));

    [Theory]
    [InlineData(200, 0)]
    [InlineData(40, 0)]
    [InlineData(20, -ListReorderLayout.AutoScrollMaxStep / 2)]
    [InlineData(0, -ListReorderLayout.AutoScrollMaxStep)]
    [InlineData(-300, -ListReorderLayout.AutoScrollMaxStep)]
    [InlineData(380, ListReorderLayout.AutoScrollMaxStep / 2)]
    [InlineData(400, ListReorderLayout.AutoScrollMaxStep)]
    [InlineData(900, ListReorderLayout.AutoScrollMaxStep)]
    public void AutoScrollStep_GrowsTowardsTheEdges(double y, double expected) =>
        Assert.Equal(expected, ListReorderLayout.AutoScrollStep(y, viewportHeight: 400), 6);
}
