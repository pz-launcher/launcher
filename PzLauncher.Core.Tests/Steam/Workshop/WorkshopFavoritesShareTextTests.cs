using PzLauncher.Core.Steam.Workshop;

namespace PzLauncher.Core.Tests.Steam.Workshop;

public sealed class WorkshopFavoritesShareTextTests
{
    [Fact]
    public void Format_WritesBothSectionsWithOneIdPerLine()
    {
        var text = WorkshopFavoritesShareText.Format([18446744073709551615, 9000], [1001]);

        Assert.Equal("#collections\n18446744073709551615\n9000\n#items\n1001\n", text);
    }

    [Fact]
    public void Format_Empty_WritesTheMarkersOnly() =>
        Assert.Equal("#collections\n#items\n", WorkshopFavoritesShareText.Format([], []));

    [Fact]
    public void Parse_FormattedText_RoundTrips()
    {
        var shared = WorkshopFavoritesShareText.Parse(WorkshopFavoritesShareText.Format([9000, 9100], [1001, 1002]));

        Assert.Equal([9000UL, 9100], shared.Collections);
        Assert.Equal([1001UL, 1002], shared.Items);
        Assert.Empty(shared.Unmarked);
        Assert.Equal(0, shared.SkippedLines);
    }

    [Fact]
    public void Parse_LinksBlankLinesCrLfAndMarkerCase_AreAccepted()
    {
        var shared = WorkshopFavoritesShareText.Parse(
            "  #Collections  \r\nhttps://steamcommunity.com/sharedfiles/filedetails/?id=9000\r\n\r\n#ITEMS\r\nsteam://url/CommunityFilePage/1001\r\n  1002  \r\n");

        Assert.Equal([9000UL], shared.Collections);
        Assert.Equal([1001UL, 1002], shared.Items);
        Assert.Equal(0, shared.SkippedLines);
    }

    [Fact]
    public void Parse_UnrecognisedLines_AreCountedAsSkipped()
    {
        var shared = WorkshopFavoritesShareText.Parse("#collections\nmy pack\n9000\n#favourites\nhttps://example.com/?id=1\n#items\n0\n1001");

        Assert.Equal([9000UL], shared.Collections);
        Assert.Equal([1001UL], shared.Items);
        Assert.Equal(4, shared.SkippedLines);
    }

    [Fact]
    public void Parse_IdsBeforeAnyMarker_AreUnmarkedUnlessASectionListsThem()
    {
        var shared = WorkshopFavoritesShareText.Parse("9000\n1001\n2002\n2002\n#items\n1001");

        Assert.Empty(shared.Collections);
        Assert.Equal([1001UL], shared.Items);
        Assert.Equal([9000UL, 2002], shared.Unmarked);
        Assert.True(shared.HasIds);
    }

    [Fact]
    public void Parse_RepeatedIdInASection_IsKeptOnce()
    {
        var shared = WorkshopFavoritesShareText.Parse("#items\n1001\n1002\n1001");

        Assert.Equal([1001UL, 1002], shared.Items);
        Assert.Equal(0, shared.SkippedLines);
    }

    [Theory]
    [InlineData("")]
    [InlineData("#collections\n#items\n")]
    [InlineData("survival pack:Core;Maps;")]
    public void Parse_NoId_HasNoIds(string text) => Assert.False(WorkshopFavoritesShareText.Parse(text).HasIds);
}
