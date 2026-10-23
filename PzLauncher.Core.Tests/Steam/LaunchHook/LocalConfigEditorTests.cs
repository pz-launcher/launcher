using System.Text;
using PzLauncher.Core.Common;
using PzLauncher.Core.Steam.LaunchHook;
using PzLauncher.Core.Steam;
using PzLauncher.Core.Tests.TestSupport;

namespace PzLauncher.Core.Tests.Steam.LaunchHook;

public sealed class LocalConfigEditorTests : IDisposable
{
    private const string EmptyOptionsLine = "\t\t\t\t\t\t\"LaunchOptions\"\t\t\"\"\n";

    private static readonly byte[] Bom = [0xEF, 0xBB, 0xBF];

    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Fixture => Fixtures.ReadLf("Steam", "localconfig.vdf");

    private static string WithOptions(string escapedValue) =>
        Fixture.Replace(EmptyOptionsLine, $"\t\t\t\t\t\t\"LaunchOptions\"\t\t\"{escapedValue}\"\n", StringComparison.Ordinal);

    private static string WithoutOptions => Fixture.Replace(EmptyOptionsLine, "", StringComparison.Ordinal);

    private string FilePath => Path.Combine(_temp.Path, "localconfig.vdf");

    private string BackupPath => FilePath + ".pzlauncher.bak";

    private string Write(string text) => WriteBytes(Encoding.UTF8.GetBytes(text));

    private string WriteBytes(byte[] bytes)
    {
        File.WriteAllBytes(FilePath, bytes);
        return FilePath;
    }

    private static async Task<LocalConfigEditor> ReadAsync(string path) =>
        ResultAssert.Ok(await LocalConfigEditor.ReadAsync(path, PzSteamApps.Game, Ct));

    private static async Task<Error> FailedAsync(string path, ErrorCode code) =>
        ResultAssert.Failed(await LocalConfigEditor.ReadAsync(path, PzSteamApps.Game, Ct), code);

    [Fact]
    public async Task ReadAsync_RealLayout_ReadsOnlyTheGamesOptions()
    {
        Assert.Contains("\"LaunchOptions\"\t\t\"not the game's\"", Fixture, StringComparison.Ordinal);

        Assert.Equal("", (await ReadAsync(Write(Fixture))).LaunchOptions);
    }

    [Fact]
    public async Task WriteAsync_LaunchOptionsInTheGamesSubBlock_IsLeftAlone()
    {
        const string cloudEnd = "\t\t\t\t\t\t}\n\t\t\t\t\t\t\"BadgeData\"";
        var original = Fixture.Replace(cloudEnd, "\t\t\t\t\t\t\t\"LaunchOptions\"\t\t\"nested\"\n" + cloudEnd, StringComparison.Ordinal);
        Assert.NotEqual(Fixture, original);
        var path = Write(original);

        ResultAssert.Ok(await (await ReadAsync(path)).WriteAsync("-debug", Ct));

        Assert.Equal(
            original.Replace(EmptyOptionsLine, "\t\t\t\t\t\t\"LaunchOptions\"\t\t\"-debug\"\n", StringComparison.Ordinal),
            await File.ReadAllTextAsync(path, Ct));
    }

    [Fact]
    public async Task ReadAsync_EscapedValue_IsUnescaped()
    {
        var editor = await ReadAsync(Write(WithOptions(@"\""C:\\Games\\PzLauncher.exe\"" %command% -a\tb")));

        Assert.Equal("\"C:\\Games\\PzLauncher.exe\" %command% -a\tb", editor.LaunchOptions);
    }

    [Fact]
    public async Task WriteAsync_ExistingKey_ChangesOnlyThatValueAndKeepsABackup()
    {
        var path = Write(Fixture);
        var editor = await ReadAsync(path);

        ResultAssert.Ok(await editor.WriteAsync("\"C:\\Games\\PzLauncher.exe\" %command% -debug", Ct));

        Assert.Equal(WithOptions(@"\""C:\\Games\\PzLauncher.exe\"" %command% -debug"), await File.ReadAllTextAsync(path, Ct));
        Assert.Equal(Fixture, await File.ReadAllTextAsync(BackupPath, Ct));
        Assert.Equal("\"C:\\Games\\PzLauncher.exe\" %command% -debug", (await ReadAsync(path)).LaunchOptions);
    }

    [Fact]
    public async Task WriteAsync_MissingKey_InsertsItBeforeTheBlockEndWithTheBlocksIndent()
    {
        var path = Write(WithoutOptions);

        ResultAssert.Ok(await (await ReadAsync(path)).WriteAsync("-debug", Ct));

        Assert.Equal(WithOptions("-debug"), await File.ReadAllTextAsync(path, Ct));
    }

    [Fact]
    public async Task WriteAsync_EmptyAppBlock_InsertsOneLevelDeeperThanItsBrace()
    {
        const string original = "\"UserLocalConfigStore\"\n{\n\t\"Software\"\n\t{\n\t\t\"Valve\"\n\t\t{\n\t\t\t\"Steam\"\n\t\t\t{\n\t\t\t\t\"apps\"\n\t\t\t\t{\n\t\t\t\t\t\"108600\"\n\t\t\t\t\t{\n\t\t\t\t\t}\n\t\t\t\t}\n\t\t\t}\n\t\t}\n\t}\n}\n";
        var path = Write(original);

        ResultAssert.Ok(await (await ReadAsync(path)).WriteAsync("-debug", Ct));

        Assert.Equal(
            original.Replace("\t\t\t\t\t{\n\t\t\t\t\t}", "\t\t\t\t\t{\n\t\t\t\t\t\t\"LaunchOptions\"\t\t\"-debug\"\n\t\t\t\t\t}", StringComparison.Ordinal),
            await File.ReadAllTextAsync(path, Ct));
    }

    [Fact]
    public async Task WriteAsync_OneLineAppBlock_InsertsBeforeTheBrace()
    {
        var path = Write("\"UserLocalConfigStore\" { \"Software\" { \"Valve\" { \"Steam\" { \"apps\" { \"108600\" { \"Playtime\" \"1\" } } } } } }");

        ResultAssert.Ok(await (await ReadAsync(path)).WriteAsync("-debug", Ct));

        Assert.Equal("-debug", (await ReadAsync(path)).LaunchOptions);
        Assert.Contains("\"Playtime\" \"1\" \"LaunchOptions\"\t\t\"-debug\" }", await File.ReadAllTextAsync(path, Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteAsync_BomAndCrLf_AreKept()
    {
        static byte[] Encode(string text) => [.. Bom, .. Encoding.UTF8.GetBytes(text.Replace("\n", "\r\n", StringComparison.Ordinal))];
        var path = WriteBytes(Encode(WithoutOptions));

        ResultAssert.Ok(await (await ReadAsync(path)).WriteAsync("-debug", Ct));

        Assert.Equal(Encode(WithOptions("-debug")), await File.ReadAllBytesAsync(path, Ct));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task WriteAsync_Unchanged_WritesNothing(bool keyExists)
    {
        var path = Write(keyExists ? WithOptions("-debug") : WithoutOptions);
        var before = File.GetLastWriteTimeUtc(path);

        ResultAssert.Ok(await (await ReadAsync(path)).WriteAsync(keyExists ? "-debug" : "", Ct));

        Assert.False(File.Exists(BackupPath));
        Assert.Equal(before, File.GetLastWriteTimeUtc(path));
    }

    [Fact]
    public async Task WriteAsync_TargetUnwritable_IsUnwritable()
    {
        var editor = await ReadAsync(Write(Fixture));
        Directory.CreateDirectory(BackupPath);

        var error = ResultAssert.Failed(await editor.WriteAsync("-debug", Ct), ErrorCode.SteamLocalConfigUnwritable);

        Assert.Equal(FilePath, error.Details["path"]);
        Assert.Equal(Fixture, await File.ReadAllTextAsync(FilePath, Ct));
    }

    [Fact]
    public async Task ReadAsync_MissingFile_IsAppNotInLocalConfig() =>
        Assert.Equal(FilePath, (await FailedAsync(FilePath, ErrorCode.SteamAppNotInLocalConfig)).Details["path"]);

    [Fact]
    public async Task ReadAsync_NoGameBlockAtTheAppsPath_IsAppNotInLocalConfig() =>
        await FailedAsync(
            Write(Fixture.Replace("\t\t\t\t\t\"108600\"\n", "\t\t\t\t\t\"108601\"\n", StringComparison.Ordinal)),
            ErrorCode.SteamAppNotInLocalConfig);

    public static TheoryData<string> Ambiguous =>
    [
        // The game's block twice.
        Fixture.Replace("\t\t\t\t\t\"108710\"\n", "\t\t\t\t\t\"108600\"\n", StringComparison.Ordinal),
        // The whole path twice, in differently cased keys.
        Fixture.Replace("\t\"WebStorage\"\n", "\t\"software\"\n\t{\n\t\t\"valve\" { \"steam\" { \"APPS\" { \"108600\" { } } } }\n\t}\n\t\"WebStorage\"\n", StringComparison.Ordinal),
        WithOptions("-a\"\n\t\t\t\t\t\t\"launchoptions\"\t\t\"-b"),
        Fixture.Replace(EmptyOptionsLine, "\t\t\t\t\t\t\"LaunchOptions\"\n\t\t\t\t\t\t{\n\t\t\t\t\t\t}\n", StringComparison.Ordinal),
        Fixture.Replace("\t\t\t\t\t\"108710\"\n", "\t\t\t\t\t\"108600\"\t\t\"x\"\n\t\t\t\t\t\"108710\"\n", StringComparison.Ordinal),
    ];

    [Theory]
    [MemberData(nameof(Ambiguous))]
    public async Task ReadAsync_AmbiguousPlace_IsMalformed(string text) =>
        await FailedAsync(Write(text), ErrorCode.SteamLocalConfigMalformed);

    [Theory]
    [InlineData("\"UserLocalConfigStore\"\n{\n\t\"a\"\t\t\"unterminated\n}\n")]
    [InlineData("\"UserLocalConfigStore\"\n{\n")]
    [InlineData("\"UserLocalConfigStore\"\n{\n}\n}\n")]
    [InlineData("\"UserLocalConfigStore\"\n{\n\t\"a\"\n}\n")]
    [InlineData("{\n}\n")]
    public async Task ReadAsync_BrokenSyntax_IsMalformed(string text) =>
        await FailedAsync(Write(text), ErrorCode.SteamLocalConfigMalformed);

    [Fact]
    public async Task ReadAsync_DeepNesting_IsMalformedWithoutOverflowingTheStack()
    {
        var text = string.Concat(Enumerable.Repeat("\"a\" { ", 100_000)) + string.Concat(Enumerable.Repeat("} ", 100_000));

        await FailedAsync(Write(text), ErrorCode.SteamLocalConfigMalformed);
    }

    [Fact]
    public async Task ReadAsync_InvalidUtf8_IsMalformed()
    {
        var bytes = Encoding.UTF8.GetBytes(Fixture);
        bytes[Array.IndexOf(bytes, (byte)'E')] = 0xFF;

        await FailedAsync(WriteBytes(bytes), ErrorCode.SteamLocalConfigMalformed);
    }

    [Fact]
    public async Task ReadAsync_CommentsAndConditions_AreSkipped()
    {
        var text = Fixture
            .Replace("\t\"Broadcast\"\n", "\t// \"108600\" { }\n\t\"Broadcast\"\n", StringComparison.Ordinal)
            .Replace("\"Playtime\"\t\t\"1234\"", "\"Playtime\"\t\t\"1234\" [$WIN32]", StringComparison.Ordinal);

        Assert.Equal("", (await ReadAsync(Write(text))).LaunchOptions);
    }
}
