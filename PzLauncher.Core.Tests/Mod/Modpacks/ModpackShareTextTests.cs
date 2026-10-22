using PzLauncher.Core.Mod.Collections;
using PzLauncher.Core.Mod.Modpacks;

namespace PzLauncher.Core.Tests.Mod.Modpacks;

public sealed class ModpackShareTextTests
{
    private static readonly SharedModCollection Preset = new("Example Server", ["ModA", "ModB", "ModC"]);

    private static readonly SharedModpack SteamPack = new("Example Server", new SharedModpackCollection.Steam(3012345678), [3012340001, 3012340002], Preset);

    private static readonly SharedModpack LocalPack = new("2042", new SharedModpackCollection.Local("My list", [3012340010, 3012340011]), [], Preset);

    private static ModpackShareTextResult.Read Read(string text) => Assert.IsType<ModpackShareTextResult.Read>(ModpackShareText.Parse(text));

    private static void AssertSame(SharedModpack expected, SharedModpack actual)
    {
        Assert.Equal(expected.Name, actual.Name);
        switch (expected.Collection)
        {
            case SharedModpackCollection.Steam steam:
                Assert.Equal(steam, actual.Collection);
                break;
            case SharedModpackCollection.Local local:
                var shared = Assert.IsType<SharedModpackCollection.Local>(actual.Collection);
                Assert.Equal(local.Name, shared.Name);
                Assert.Equal(local.Items, shared.Items);
                break;
        }

        Assert.Equal(expected.ExtraItems, actual.ExtraItems);
        Assert.Equal(expected.Preset.Name, actual.Preset.Name);
        Assert.Equal(expected.Preset.ModIds, actual.Preset.ModIds);
    }

    [Fact]
    public void Format_SteamCollection_WritesEverySectionWithThePresetLast() =>
        Assert.Equal(
            "#pzlauncher-modpack 1\n#name\nExample Server\n#steam-collection\n3012345678\n#items\n3012340001\n3012340002\n#preset\nExample Server:ModA;ModB;ModC;\n",
            ModpackShareText.Format(SteamPack));

    [Fact]
    public void Format_LocalCollectionWithoutExtraItems_WritesItsNameAndItemsAndNoItemsSection() =>
        Assert.Equal(
            "#pzlauncher-modpack 1\n#name\n2042\n#local-collection\nMy list\n3012340010\n3012340011\n#preset\nExample Server:ModA;ModB;ModC;\n",
            ModpackShareText.Format(LocalPack));

    [Fact]
    public void Format_LastLine_IsTheGamesPresetLine()
    {
        var lastLine = ModpackShareText.Format(SteamPack).TrimEnd('\n').Split('\n')[^1];

        var preset = Assert.Single(ModCollectionShareText.Parse(lastLine));
        Assert.Equal(Preset.Name, preset.Name);
        Assert.Equal(Preset.ModIds, preset.ModIds);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Parse_FormattedText_RoundTrips(bool steam)
    {
        var modpack = steam ? SteamPack : LocalPack;

        var read = Read(ModpackShareText.Format(modpack));

        AssertSame(modpack, read.Modpack);
        Assert.Equal(0, read.SkippedLines);
    }

    [Fact]
    public void Parse_MarkerCaseWhitespaceCrLfAndBlankLines_AreAccepted()
    {
        var read = Read("\r\n  #PzLauncher-Modpack   1 \r\n#NAME\r\n  Example Server  \r\n\r\n#Steam-Collection\r\n3012345678\r\n#Items\r\n3012340001\r\n3012340002\r\n#PRESET\r\nExample Server:ModA;ModB;ModC;\r\n");

        AssertSame(SteamPack, read.Modpack);
    }

    [Fact]
    public void Parse_WorkshopLinks_AreReadAsIds()
    {
        var read = Read(
            "#pzlauncher-modpack 1\n#name\nP\n#steam-collection\nhttps://steamcommunity.com/sharedfiles/filedetails/?id=3012345678\n"
            + "#items\nsteam://url/CommunityFilePage/3012340001\n#preset\nP:A;\n");

        Assert.Equal(new SharedModpackCollection.Steam(3012345678), read.Modpack.Collection);
        Assert.Equal([3012340001UL], read.Modpack.ExtraItems);
    }

    [Fact]
    public void Parse_UnreadableAndRepeatedIds_AreSkippedAndCountedOrKeptOnce()
    {
        var read = Read(
            "#pzlauncher-modpack 1\nstray line\n#name\nP\n#local-collection\nL\n1\nnot an id\n2\n1\n#items\n3\nhttps://example.com/?id=4\n3\n#preset\nP:A;\nafter the preset\n");

        var local = Assert.IsType<SharedModpackCollection.Local>(read.Modpack.Collection);
        Assert.Equal([1UL, 2], local.Items);
        Assert.Equal([3UL], read.Modpack.ExtraItems);
        Assert.Equal(4, read.SkippedLines);
    }

    [Fact]
    public void Parse_SecondSteamCollectionId_IsSkipped()
    {
        var read = Read("#pzlauncher-modpack 1\n#name\nP\n#steam-collection\n10\n20\n#preset\nP:A;\n");

        Assert.Equal(new SharedModpackCollection.Steam(10), read.Modpack.Collection);
        Assert.Equal(1, read.SkippedLines);
    }

    [Fact]
    public void Parse_UnknownMarker_ItsSectionIsIgnored()
    {
        var read = Read("#pzlauncher-modpack 1\n#name\nP\n#server\n203.0.113.5:16261\npassword\n#steam-collection\n10\n#preset\nP:A;\n");

        Assert.Equal(new SharedModpackCollection.Steam(10), read.Modpack.Collection);
        Assert.Equal(0, read.SkippedLines);
    }

    [Theory]
    [InlineData("3012345678")]
    [InlineData("#items")]
    public void Parse_NameLines_AreNamesEvenWhenTheyLookLikeIdsOrMarkers(string name)
    {
        var read = Read($"#pzlauncher-modpack 1\n#name\n{name}\n#local-collection\n{name}\n10\n#preset\nP:A;\n");

        Assert.Equal(name, read.Modpack.Name);
        var local = Assert.IsType<SharedModpackCollection.Local>(read.Modpack.Collection);
        Assert.Equal(name, local.Name);
        Assert.Equal([10UL], local.Items);
    }

    [Fact]
    public void Parse_NewerVersion_IsRefused() =>
        Assert.Equal(new ModpackShareTextResult.NewerVersion(2), ModpackShareText.Parse("#pzlauncher-modpack 2\n#everything-else\nchanged\n"));

    [Theory]
    [InlineData("")]
    [InlineData("Example Server:ModA;ModB;\n")]
    [InlineData("#collections\n9000\n")]
    [InlineData("#name\nP\n#pzlauncher-modpack 1\n")]
    public void Parse_NoHeaderFirst_IsNotModpackText(string text) =>
        Assert.IsType<ModpackShareTextResult.NotModpack>(ModpackShareText.Parse(text));

    [Theory]
    [InlineData("#pzlauncher-modpack\n#name\nP\n#steam-collection\n10\n#preset\nP:A;\n")]
    [InlineData("#pzlauncher-modpack one\n#name\nP\n#steam-collection\n10\n#preset\nP:A;\n")]
    [InlineData("#pzlauncher-modpack 0\n#name\nP\n#steam-collection\n10\n#preset\nP:A;\n")]
    [InlineData("#pzlauncher-modpack 1\n#steam-collection\n10\n#preset\nP:A;\n")]
    [InlineData("#pzlauncher-modpack 1\n#name\nP\n#preset\nP:A;\n")]
    [InlineData("#pzlauncher-modpack 1\n#name\nP\n#steam-collection\nnot an id\n#preset\nP:A;\n")]
    [InlineData("#pzlauncher-modpack 1\n#name\nP\n#steam-collection\n10\n")]
    [InlineData("#pzlauncher-modpack 1\n#name\nP\n#steam-collection\n10\n#preset\nno separator\n")]
    [InlineData("#pzlauncher-modpack 1\n#name\nP\n#steam-collection\n10\n#preset\n")]
    [InlineData("#pzlauncher-modpack 1\n#name\nP\n#local-collection\n")]
    [InlineData("#pzlauncher-modpack 1\n#name\nP\n#steam-collection\n10\n#local-collection\nL\n#preset\nP:A;\n")]
    [InlineData("#pzlauncher-modpack 1\n#name\nP\n#name\nQ\n#steam-collection\n10\n#preset\nP:A;\n")]
    [InlineData("#pzlauncher-modpack 1\n#name\nP\n#steam-collection\n10\n#preset\nP:A;\n#PRESET\nQ:B;\n")]
    public void Parse_MissingOrRepeatedSection_IsIncomplete(string text) =>
        Assert.IsType<ModpackShareTextResult.Incomplete>(ModpackShareText.Parse(text));
}
