using PzLauncher.Core.Common;
using PzLauncher.Core.Steam;
using PzLauncher.Core.Tests.TestSupport;
using ValveKeyValue;

namespace PzLauncher.Core.Tests.Steam;

public class SteamKeyValuesTests
{
    private static KVObject Parse(string text) => ResultAssert.Ok(SteamKeyValues.Parse(text));

    private static KVObject ParseChild(string text, string key)
    {
        var child = Parse(text).GetChild(key);
        Assert.NotNull(child);
        return child;
    }

    [Fact]
    public void Parse_RealLibraryFolders_ReadsLibrariesWithUnescapedPaths()
    {
        var folders = ParseChild(Fixtures.Read("Steam", "libraryfolders.vdf"), "libraryfolders");

        Assert.Equal(2, folders.GetChildren().Count);
        Assert.Equal(@"C:\Program Files (x86)\Steam", folders.GetChild("0")?.GetString("path"));
        Assert.Equal(@"E:\SteamLibrary", folders.GetChild("1")?.GetString("path"));
        Assert.Equal("10335862594", folders.GetChild("1")?.GetChild("apps")?.GetString("108600"));
    }

    [Fact]
    public void Parse_RealAppManifest_ReadsAppState()
    {
        var state = ParseChild(Fixtures.Read("Steam", "appmanifest_108600.acf"), "AppState");

        Assert.Equal("108600", state.GetString("appid"));
        Assert.Equal("ProjectZomboid", state.GetString("installdir"));
        Assert.Equal("25485521", state.GetString("buildid"));
        Assert.Equal("english", state.GetChild("UserConfig")?.GetString("language"));
        Assert.Null(state.GetChild("UserConfig")?.GetString("BetaKey"));
    }

    [Theory]
    [InlineData("D:\\\\Games\\\\Steam Library", @"D:\Games\Steam Library")]
    [InlineData("say \\\"hi\\\"\\n\\tok", "say \"hi\"\n\tok")]
    public void Parse_EscapedValue_Unescapes(string written, string expected) =>
        Assert.Equal(expected, ParseChild($"\"r\" {{ \"v\" \"{written}\" }}", "r").GetString("v"));

    [Fact]
    public void Parse_TopLevelStringValue_IsReadable() => Assert.Equal("v", Parse("\"k\" \"v\"").GetString("k"));

    [Fact]
    public void Parse_NestedBlocks_BuildsTree()
    {
        var root = ParseChild("\"root\" { \"a\" { \"b\" { \"c\" \"deep\" } } \"sibling\" \"x\" }", "root");

        Assert.Equal("deep", root.GetChild("a")?.GetChild("b")?.GetString("c"));
        Assert.Equal("x", root.GetString("sibling"));
        Assert.Equal(["a", "sibling"], root.GetChildren().Select(c => c.Key));
    }

    [Fact]
    public void GetChild_DuplicateKeyInOtherCasing_ReturnsLastAndCollapsesInChildren()
    {
        var r = ParseChild("\"r\" { \"k\" \"first\" \"other\" \"o\" \"K\" \"second\" }", "R");

        Assert.Equal("second", r.GetString("k"));
        var children = r.GetChildren();
        Assert.Equal(2, children.Count);
        Assert.Equal("second", children[0].Value.AsText());
    }

    [Fact]
    public void Parse_CommentsAndUnquotedTokensAndConditionals_AreHandled()
    {
        const string text = """
            // leading comment
            root
            {
                key value // trailing comment
                "cond" "win" [$WIN32]
                "notwin" "dropped" [!$WIN32]
                child { inner 1 }
            }
            """;

        var root = ParseChild(text, "root");

        Assert.Equal("value", root.GetString("key"));
        // ValveKeyValue treats $WIN32 as true on every OS (its default conditions).
        Assert.Equal("win", root.GetString("cond"));
        Assert.Null(root.GetChild("notwin"));
        Assert.Equal("1", root.GetChild("child")?.GetString("inner"));
    }

    [Fact]
    public void Parse_ByteOrderMark_IsIgnored() => Assert.Equal("v", ParseChild("\uFEFF\"k\" { \"a\" \"v\" }", "k").GetString("a"));

    // Known limitation (design.md "VDF parsing"): pinned so a library upgrade that changes it is noticed.
    // The forms Steam writes (plain integers) still round-trip exactly.
    [Theory]
    [InlineData("25485521", "25485521")]
    [InlineData("10335862594", "10335862594")]
    [InlineData("0", "0")]
    [InlineData("-1", "-1")]
    [InlineData("007", "7")]
    [InlineData("+5", "5")]
    [InlineData("1.0", "1")]
    [InlineData("1.50", "1.5")]
    [InlineData("1e5", "100000")]
    [InlineData("41.78", "41.78")]
    [InlineData("41.78.16", "41.78.16")]
    [InlineData("b42", "b42")]
    public void GetString_NumberLikeValue_ReturnsLibraryNormalizedText(string written, string expected) =>
        Assert.Equal(expected, ParseChild($"\"r\" {{ \"v\" \"{written}\" }}", "r").GetString("v"));

    [Fact]
    public void GetString_OnCollectionOrMissingKey_ReturnsNull()
    {
        var r = ParseChild("\"r\" { \"obj\" { \"a\" \"b\" } }", "r");
        var scalar = r.GetChild("obj")?.GetChild("a");

        Assert.NotNull(scalar);
        Assert.Null(r.GetString("obj"));
        Assert.Null(r.GetString("missing"));
        Assert.Null(scalar.GetChild("anything"));
        Assert.Empty(scalar.GetChildren());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \n // only a comment\n")]
    [InlineData("\"root\" { \"k\" \"v\"")]
    [InlineData("\"root\" { \"k\" \"unterminated }")]
    [InlineData("\"root\" { } }")]
    [InlineData("\"root\" { { } }")]
    [InlineData("\"root\" { \"k\" }")]
    [InlineData("\"lonely-key\"")]
    [InlineData("\"a\" { } \"b\" { }")]
    [InlineData("\"root\" { \"k\" \"bad \\q escape\" }")]
    [InlineData("\"root\" { \"k\" \"v\" [$WIN32 }")]
    [InlineData("#base \"other.vdf\"\n\"root\" { }")]
    [InlineData("#include \"other.vdf\"\n\"root\" { }")]
    [InlineData("\"root\" { #include \"other.vdf\" }")]
    public void Parse_MalformedInput_ReturnsVdfMalformed(string text)
    {
        var error = ResultAssert.Failed(SteamKeyValues.Parse(text), ErrorCode.SteamVdfMalformed);

        Assert.False(string.IsNullOrEmpty(error.Details["reason"]));
    }

    [Fact]
    public void Parse_VeryDeepNesting_DoesNotOverflowTheStack()
    {
        // ValveKeyValue's reader is iterative; if a future version recursed, crashing the test host is the signal.
        const int depth = 100_000;

        Parse(string.Concat(Enumerable.Repeat("\"k\" { ", depth)) + new string('}', depth));
    }

    [Fact]
    public void Parse_VeryDeepUnclosedNesting_ReturnsVdfMalformed() =>
        ResultAssert.Failed(SteamKeyValues.Parse(string.Concat(Enumerable.Repeat("\"k\" { ", 100_000))), ErrorCode.SteamVdfMalformed);
}
