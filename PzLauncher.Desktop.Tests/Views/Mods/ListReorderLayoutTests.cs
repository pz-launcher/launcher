using PzLauncher.Desktop.Views.Mods;

namespace PzLauncher.Desktop.Tests.Views.Mods;

public sealed class ListReorderLayoutTests
{
    private static readonly ListReorderLayout Uniform = new([10, 10, 10, 10]);

    private static readonly ListReorderLayout Mixed = new([10, 20, 30]);

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
        Assert.Equal([0d, 10d, 30d], Enumerable.Range(0, Mixed.Count).Select(Mixed.TopOf));
        Assert.Equal(60, Mixed.TotalHeight);
    }

    [Theory]
    [InlineData(15, 1)]
    [InlineData(24.9, 1)]
    [InlineData(25.1, 2)]
    [InlineData(35.1, 3)]
    [InlineData(500, 3)]
    [InlineData(5.1, 1)]
    [InlineData(4.9, 0)]
    [InlineData(-50, 0)]
    public void TargetIndex_PassesARowWhenTheDraggedMiddleCrossesItsMiddle(double center, int expected) =>
        Assert.Equal(expected, Uniform.TargetIndex(from: 1, center));

    [Fact]
    public void ShiftOf_DraggingDown_SlidesTheRowsInBetweenUp()
    {
        Assert.Equal([0d, 0d, -10d, -10d], Enumerable.Range(0, 4).Select(i => Uniform.ShiftOf(i, from: 1, to: 3)));
    }

    [Fact]
    public void ShiftOf_DraggingUp_SlidesTheRowsInBetweenDownByTheDraggedHeight()
    {
        Assert.Equal([0d, 30d, 0d], Enumerable.Range(0, 3).Select(i => Mixed.ShiftOf(i, from: 2, to: 1)));
        Assert.Equal([30d, 30d, 0d], Enumerable.Range(0, 3).Select(i => Mixed.ShiftOf(i, from: 2, to: 0)));
    }

    [Fact]
    public void ShiftOf_InPlace_SlidesNothing()
    {
        Assert.All(Enumerable.Range(0, 4), i => Assert.Equal(0, Uniform.ShiftOf(i, from: 2, to: 2)));
    }

    [Theory]
    [InlineData(0, 2, 50)]
    [InlineData(0, 1, 20)]
    [InlineData(2, 0, 0)]
    [InlineData(2, 1, 10)]
    [InlineData(1, 1, 10)]
    public void LandingTop_IsTheRowsTopAfterTheMove(int from, int to, double expected) =>
        Assert.Equal(expected, Mixed.LandingTop(from, to));

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
