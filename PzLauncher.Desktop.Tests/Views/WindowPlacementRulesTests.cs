using Avalonia;
using PzLauncher.Desktop.Services;
using PzLauncher.Desktop.Views;

namespace PzLauncher.Desktop.Tests.Views;

public sealed class WindowPlacementRulesTests
{
    private static readonly Size MinSize = new(820, 520);

    private static readonly ScreenArea Primary = new(new PixelRect(0, 0, 1920, 1040), 1, IsPrimary: true);

    // Left of the primary at 150%, so its coordinates are negative.
    private static readonly ScreenArea Left = new(new PixelRect(-2560, 0, 2560, 1400), 1.5, IsPrimary: false);

    private static readonly ScreenArea[] Screens = [Left, Primary];

    private static WindowPlacement Saved(int x, int y, double width = 1000, double height = 640, bool maximized = false) =>
        new(x, y, width, height, maximized);

    [Theory]
    [InlineData(100, 50)]
    [InlineData(-2000, 100)]
    // Straddling both screens.
    [InlineData(-900, 10)]
    // Hanging off the bottom: the title bar's top 20 px are still on screen.
    [InlineData(300, 1020)]
    public void Resolve_TitleBarOnAScreen_KeepsPositionAndSize(int x, int y)
    {
        var startup = WindowPlacementRules.Resolve(Saved(x, y), Screens, MinSize);

        Assert.Equal(new WindowStartup(new PixelPoint(x, y), new Size(1000, 640), false), startup);
    }

    [Theory]
    [InlineData(-30000, -30000)]
    [InlineData(5000, 100)]
    // Only 50 px of the title bar on screen.
    [InlineData(1870, 100)]
    // Only 10 px of the title bar on screen.
    [InlineData(300, 1030)]
    // Title bar above every screen, body still visible.
    [InlineData(300, -40)]
    public void Resolve_TitleBarOffEveryScreen_CentersOnPrimaryWithSavedSize(int x, int y)
    {
        var startup = WindowPlacementRules.Resolve(Saved(x, y), Screens, MinSize);

        Assert.Equal(new WindowStartup(new PixelPoint(460, 200), new Size(1000, 640), false), startup);
    }

    [Fact]
    public void Resolve_TitleBarUnderMinimumOnOneScreen_ClampsToTheOther()
    {
        // 90 px on the left screen is under its 150 px minimum.
        var startup = WindowPlacementRules.Resolve(Saved(-90, 0, width: 5000, height: 5000), Screens, MinSize);

        Assert.Equal(new WindowStartup(new PixelPoint(-90, 0), new Size(1920, 1040), false), startup);
    }

    [Fact]
    public void Resolve_TitleBarOnScreenOnlyBeforeClamping_CentersOnPrimary()
    {
        var startup = WindowPlacementRules.Resolve(Saved(-3000, 100, width: 5000), [Primary], MinSize);

        Assert.Equal(new WindowStartup(new PixelPoint(0, 200), new Size(1920, 640), false), startup);
    }

    [Fact]
    public void Resolve_LargerThanScreen_ClampsToWorkingAreaInDips()
    {
        var startup = WindowPlacementRules.Resolve(Saved(-2500, 20, width: 3000, height: 3000), Screens, MinSize);

        Assert.Equal(new Size(2560 / 1.5, 1400 / 1.5), startup.Size);
    }

    [Fact]
    public void Resolve_SmallerThanMinimum_ClampsToMinimum()
    {
        var startup = WindowPlacementRules.Resolve(Saved(100, 50, width: 400, height: 300), Screens, MinSize);

        Assert.Equal(MinSize, startup.Size);
    }

    [Fact]
    public void Resolve_OffScreenAndLargerThanPrimary_CentersClampedSize()
    {
        var startup = WindowPlacementRules.Resolve(Saved(-30000, 0, width: 4000, height: 4000), Screens, MinSize);

        Assert.Equal(new WindowStartup(new PixelPoint(0, 0), new Size(1920, 1040), false), startup);
    }

    [Theory]
    [InlineData(100, true)]
    [InlineData(100, false)]
    [InlineData(-30000, true)]
    [InlineData(-30000, false)]
    public void Resolve_Maximized_CarriesFlag(int x, bool maximized)
    {
        Assert.Equal(maximized, WindowPlacementRules.Resolve(Saved(x, 50, maximized: maximized), Screens, MinSize).IsMaximized);
    }

    [Theory]
    [InlineData(false, 460, 200)]
    // 1000x640 DIP is 1500x960 px at 150%.
    [InlineData(true, -2030, 220)]
    public void Center_DefaultSize_CentersInThatScreensWorkingArea(bool left, int x, int y)
    {
        var bounds = WindowPlacementRules.Center(new Size(1000, 640), left ? Left : Primary, MinSize, default);

        Assert.Equal(new WindowBounds(new PixelPoint(x, y), new Size(1000, 640)), bounds);
    }

    [Fact]
    public void Center_ClientOffset_CentersTheClientArea()
    {
        var bounds = WindowPlacementRules.Center(new Size(1000, 640), Left, MinSize, new PixelVector(11, 0));

        Assert.Equal(new PixelPoint(-2041, 220), bounds.Position);
    }

    [Fact]
    public void Center_TallerThanScreen_ClampsToTheWorkingArea()
    {
        var screen = new ScreenArea(new PixelRect(100, 50, 1800, 900), 1.5, IsPrimary: false);

        var bounds = WindowPlacementRules.Center(new Size(1000, 640), screen, MinSize, default);

        Assert.Equal(new WindowBounds(new PixelPoint(250, 50), new Size(1000, 600)), bounds);
    }

    [Fact]
    public void Resolve_NoScreens_LeavesPositionToTheWindow()
    {
        var startup = WindowPlacementRules.Resolve(Saved(100, 50, width: 400, height: 640, maximized: true), [], MinSize);

        Assert.Equal(new WindowStartup(null, new Size(820, 640), true), startup);
    }
}
