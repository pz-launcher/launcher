using PzLauncher.Core.Common;
using PzLauncher.Core.Steam;
using PzLauncher.Core.Tests.TestSupport;

namespace PzLauncher.Core.Tests.Steam;

public class SteamKeyValuesTests
{
    [Fact]
    public void Parse_RealLibraryFolders_ReadsLibrariesWithUnescapedPaths()
    {
        var result = SteamKeyValues.Parse(Fixtures.Read("Steam", "libraryfolders.vdf"));

        Assert.True(result.IsSuccess);
        var folders = result.Value.GetChild("libraryfolders")!;
        Assert.Equal(2, folders.GetChildren().Count);
        Assert.Equal(@"C:\Program Files (x86)\Steam", folders.GetChild("0")!.GetString("path"));
        Assert.Equal(@"E:\SteamLibrary", folders.GetChild("1")!.GetString("path"));
        Assert.Equal("10335862594", folders.GetChild("1")!.GetChild("apps")!.GetString("108600"));
    }

    [Fact]
    public void Parse_RealAppManifest_ReadsAppState()
    {
        var result = SteamKeyValues.Parse(Fixtures.Read("Steam", "appmanifest_108600.acf"));

        Assert.True(result.IsSuccess);
        var state = result.Value.GetChild("AppState")!;
        Assert.Equal("108600", state.GetString("appid"));
        Assert.Equal("ProjectZomboid", state.GetString("installdir"));
        Assert.Equal("25485521", state.GetString("buildid"));
        Assert.Equal("english", state.GetChild("UserConfig")!.GetString("language"));
        Assert.Null(state.GetChild("UserConfig")!.GetString("BetaKey"));
    }

    [Fact]
    public void Parse_EscapedBackslashPath_Unescapes()
    {
        var result = SteamKeyValues.Parse("\"a\" { \"path\" \"D:\\\\Games\\\\Steam Library\" }");

        Assert.True(result.IsSuccess);
        Assert.Equal(@"D:\Games\Steam Library", result.Value.GetChild("a")!.GetString("path"));
    }

    [Fact]
    public void Parse_OtherEscapes_Unescapes()
    {
        var result = SteamKeyValues.Parse("\"k\" { \"v\" \"say \\\"hi\\\"\\n\\tok\" }");

        Assert.True(result.IsSuccess);
        Assert.Equal("say \"hi\"\n\tok", result.Value.GetChild("k")!.GetString("v"));
    }

    [Fact]
    public void Parse_TopLevelStringValue_IsReadable()
    {
        var result = SteamKeyValues.Parse("\"k\" \"v\"");

        Assert.True(result.IsSuccess);
        Assert.Equal("v", result.Value.GetString("k"));
    }

    [Fact]
    public void Parse_NestedBlocks_BuildsTree()
    {
        const string text = """
            "root"
            {
                "a"
                {
                    "b"
                    {
                        "c"  "deep"
                    }
                }
                "sibling"  "x"
            }
            """;

        var result = SteamKeyValues.Parse(text);

        Assert.True(result.IsSuccess);
        var root = result.Value.GetChild("root")!;
        Assert.Equal("deep", root.GetChild("a")!.GetChild("b")!.GetString("c"));
        Assert.Equal("x", root.GetString("sibling"));
        Assert.Equal(["a", "sibling"], root.GetChildren().Select(c => c.Key));
    }

    [Fact]
    public void Parse_DuplicateKeys_KeepsLast()
    {
        var result = SteamKeyValues.Parse("\"r\" { \"k\" \"first\" \"other\" \"o\" \"K\" \"second\" }");

        Assert.True(result.IsSuccess);
        var r = result.Value.GetChild("r")!;
        Assert.Equal("second", r.GetString("k"));
        var children = r.GetChildren();
        Assert.Equal(2, children.Count);
        Assert.Equal("second", children[0].Value.AsText());
    }

    [Fact]
    public void Parse_KeyLookup_IsCaseInsensitive()
    {
        var result = SteamKeyValues.Parse("\"AppState\" { \"UserConfig\" { \"betakey\" \"unstable\" } }");

        Assert.True(result.IsSuccess);
        Assert.Equal("unstable", result.Value.GetChild("appstate")!.GetChild("userconfig")!.GetString("BetaKey"));
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

        var result = SteamKeyValues.Parse(text);

        Assert.True(result.IsSuccess);
        var root = result.Value.GetChild("root")!;
        Assert.Equal("value", root.GetString("key"));
        // ValveKeyValue treats $WIN32 as true on every OS (its default conditions).
        Assert.Equal("win", root.GetString("cond"));
        Assert.Null(root.GetChild("notwin"));
        Assert.Equal("1", root.GetChild("child")!.GetString("inner"));
    }

    [Fact]
    public void Parse_ByteOrderMark_IsIgnored()
    {
        var result = SteamKeyValues.Parse("\uFEFF\"k\" { \"a\" \"v\" }");

        Assert.True(result.IsSuccess);
        Assert.Equal("v", result.Value.GetChild("k")!.GetString("a"));
    }

    [Fact]
    public void Parse_NumericValues_AreReturnedAsWritten()
    {
        // ValveKeyValue stores these as numbers; the forms Steam writes still round-trip exactly.
        var result = SteamKeyValues.Parse("\"r\" { \"buildid\" \"25485521\" \"size\" \"10335862594\" \"zero\" \"0\" \"neg\" \"-1\" }");

        Assert.True(result.IsSuccess);
        var r = result.Value.GetChild("r")!;
        Assert.Equal("25485521", r.GetString("buildid"));
        Assert.Equal("10335862594", r.GetString("size"));
        Assert.Equal("0", r.GetString("zero"));
        Assert.Equal("-1", r.GetString("neg"));
    }

    [Theory]
    [InlineData("007", "7")]
    [InlineData("+5", "5")]
    [InlineData("1.0", "1")]
    [InlineData("1.50", "1.5")]
    [InlineData("1e5", "100000")]
    [InlineData("41.78", "41.78")]
    [InlineData("41.78.16", "41.78.16")]
    [InlineData("b42", "b42")]
    public void GetString_NumberLikeValue_ReturnsLibraryNormalizedText(string written, string expected)
    {
        // Known limitation (design.md "VDF parsing"); pinned so a library upgrade that changes it is noticed.
        var result = SteamKeyValues.Parse($"\"r\" {{ \"v\" \"{written}\" }}");

        Assert.True(result.IsSuccess);
        Assert.Equal(expected, result.Value.GetChild("r")!.GetString("v"));
    }

    [Fact]
    public void GetString_OnCollectionOrMissingKey_ReturnsNull()
    {
        var result = SteamKeyValues.Parse("\"r\" { \"obj\" { \"a\" \"b\" } }");

        Assert.True(result.IsSuccess);
        var r = result.Value.GetChild("r")!;
        Assert.Null(r.GetString("obj"));
        Assert.Null(r.GetString("missing"));
        Assert.Null(r.GetChild("obj")!.GetChild("a")!.GetChild("anything"));
        Assert.Empty(r.GetChild("obj")!.GetChild("a")!.GetChildren());
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
        var result = SteamKeyValues.Parse(text);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.SteamVdfMalformed, result.Error!.Code);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
        Assert.False(string.IsNullOrEmpty(result.Error.Details["reason"]));
    }

    [Fact]
    public void Parse_VeryDeepNesting_DoesNotOverflowTheStack()
    {
        // ValveKeyValue's reader is iterative; if a future version recursed, crashing the test host is the signal.
        const int depth = 100_000;
        var text = string.Concat(Enumerable.Repeat("\"k\" { ", depth)) + new string('}', depth);

        Assert.True(SteamKeyValues.Parse(text).IsSuccess);
    }

    [Fact]
    public void Parse_VeryDeepUnclosedNesting_ReturnsVdfMalformed()
    {
        var text = string.Concat(Enumerable.Repeat("\"k\" { ", 100_000));

        var result = SteamKeyValues.Parse(text);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.SteamVdfMalformed, result.Error!.Code);
    }
}
