using PzLauncher.Core.Mod.Collections;

namespace PzLauncher.Core.Tests.Mod.Collections;

public class ModCollectionShareTextTests
{
    [Fact]
    public void Format_IsTheGamesShareText() =>
        Assert.Equal("B42·20通用合集:ModA;ModB;\n", ModCollectionShareText.Format("B42·20通用合集", ["ModA", "ModB"]));

    [Fact]
    public void Format_EmptyCollection_IsNameColonNewline() =>
        Assert.Equal("empty:\n", ModCollectionShareText.Format("empty", []));

    [Fact]
    public void Parse_FormattedText_RoundTripsNameAndOrder()
    {
        var shared = Assert.Single(ModCollectionShareText.Parse(ModCollectionShareText.Format("pack", ["Z", "A", "M"])));

        Assert.Equal("pack", shared.Name);
        Assert.Equal(["Z", "A", "M"], shared.ModIds);
    }

    [Fact]
    public void Parse_SeveralLines_SkipsFavouritesBlankAndNamelessLinesAndDuplicateIds()
    {
        var shared = ModCollectionShareText.Parse("  first:A;B;A;;\r\n!fav!:X;\n\n:Orphan;\nno separator\nsecond: C ;\n");

        Assert.Equal(["first", "second"], shared.Select(s => s.Name));
        Assert.Equal(["A", "B"], shared[0].ModIds);
        Assert.Equal(["C"], shared[1].ModIds);
    }

    [Theory]
    [InlineData("B42·20通用合集", true)]
    [InlineData("my pack (1)", true)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("a:b", false)]
    [InlineData("a/b", false)]
    [InlineData("a\\b", false)]
    [InlineData("a*b", false)]
    [InlineData("a?b", false)]
    [InlineData("a\"b", false)]
    [InlineData("a<b", false)]
    [InlineData("a>b", false)]
    [InlineData("a|b", false)]
    [InlineData("a\nb", false)]
    [InlineData("a\tb", false)]
    [InlineData("!fav!", false)]
    public void IsValid_Name_FollowsTheGamesRules(string name, bool valid) =>
        Assert.Equal(valid, ModCollectionName.IsValid(name));
}
