using PzLauncher.Core.Steam.Workshop.Ids;
using PzLauncher.Core.Steam.Workshop;

namespace PzLauncher.Core.Tests.Steam.Workshop.Ids;

public sealed class WorkshopLinksTests
{
    [Theory]
    [InlineData("2409333430")]
    [InlineData("  2409333430 \n")]
    [InlineData("https://steamcommunity.com/sharedfiles/filedetails/?id=2409333430")]
    [InlineData("https://steamcommunity.com/sharedfiles/filedetails/?id=2409333430&searchtext=cars")]
    [InlineData("https://steamcommunity.com/sharedfiles/filedetails/?searchtext=cars&id=2409333430")]
    [InlineData("https://steamcommunity.com/workshop/filedetails/?id=2409333430")]
    [InlineData("http://steamcommunity.com/sharedfiles/filedetails/?id=2409333430")]
    [InlineData("https://www.steamcommunity.com/sharedfiles/filedetails/?ID=2409333430")]
    [InlineData("steamcommunity.com/sharedfiles/filedetails/?id=2409333430")]
    [InlineData("steam://url/CommunityFilePage/2409333430")]
    public void TryParseItemId_IdOrWorkshopLink_ReturnsTheId(string text)
    {
        Assert.True(WorkshopLinks.TryParseItemId(text, out var id));
        Assert.Equal(2409333430UL, id);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("12ab")]
    [InlineData("18446744073709551616")]
    [InlineData("https://steamcommunity.com/sharedfiles/filedetails/")]
    [InlineData("https://steamcommunity.com/sharedfiles/filedetails/?id=abc")]
    [InlineData("https://example.com/sharedfiles/filedetails/?id=2409333430")]
    [InlineData("https://steamcommunity.com.example.com/?id=2409333430")]
    [InlineData("ftp://steamcommunity.com/?id=2409333430")]
    [InlineData("steam://url/CommunityFilePage/")]
    public void TryParseItemId_NoItemId_ReturnsFalse(string text)
    {
        Assert.False(WorkshopLinks.TryParseItemId(text, out var id));
        Assert.Equal(0UL, id);
    }

    [Fact]
    public void ParseLines_IdsAndLinks_KeepsEachIdOnceInOrderAndCountsTheOtherLines()
    {
        var parsed = WorkshopLinks.ParseLines(
            "3\r\n\n  https://steamcommunity.com/sharedfiles/filedetails/?id=1  \nnot an id\n3\nsteam://url/CommunityFilePage/2\n#items\n");

        Assert.Equal([3UL, 1, 2], parsed.Ids);
        Assert.Equal(2, parsed.SkippedLines);
    }

    [Fact]
    public void ClientPage_OpensTheItemPageInSteam()
    {
        Assert.Equal("steam://url/CommunityFilePage/18446744073709551615", WorkshopLinks.ClientPage(ulong.MaxValue).OriginalString);
    }
}
