using System.Text;
using PzLauncher.Core.Common;
using PzLauncher.Core.Mod.Collections.Files;
using PzLauncher.Core.Mod.Collections;
using PzLauncher.Core.Tests.TestSupport;

namespace PzLauncher.Core.Tests.Mod.Collections.Files;

public sealed class ActiveModsFileTests : IDisposable
{
    // The shape the game writes on Windows.
    private const string GameFile =
        "VERSION = 1,\r\n\r\nmods\r\n{\r\n    mod = ZombieBuddy,\r\n    mod = ModA,\r\n}\r\n\r\nmaps\r\n{\r\n    map = Muldraugh, KY,\r\n}\r\n";

    private readonly TempDirectory _temp = new();
    private readonly string _cache;

    public ActiveModsFileTests()
    {
        _cache = _temp.CreateDirectory("Zomboid");
    }

    public void Dispose() => _temp.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string FilePath => Path.Combine(_cache, "mods", "default.txt");

    private void Write(string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, text, new UTF8Encoding(false));
    }

    private string Text() => File.ReadAllText(FilePath);

    private async Task<ActiveModsFile> ReadAsync(string newline = "\r\n") => ResultAssert.Ok(await ActiveModsFile.ReadAsync(_cache, newline, Ct));

    private async Task ApplyAsync(params string[] ids)
    {
        var file = await ReadAsync();
        file.SetModIds(ids);
        ResultAssert.Ok(await file.WriteAsync(Ct));
    }

    [Fact]
    public async Task ModIds_GameFile_InLoadOrder()
    {
        Write(GameFile);

        Assert.Equal(["ZombieBuddy", "ModA"], (await ReadAsync()).ModIds);
    }

    [Fact]
    public async Task ModIds_MissingFile_IsEmpty() =>
        Assert.Empty((await ReadAsync()).ModIds);

    [Fact]
    public async Task SetModIds_GameFile_RewritesOnlyTheModsBlockWithCrlf()
    {
        Write(GameFile);

        await ApplyAsync("B", "C");

        Assert.Equal(
            "VERSION = 1,\r\n\r\nmods\r\n{\r\n    mod = B,\r\n    mod = C,\r\n}\r\n\r\nmaps\r\n{\r\n    map = Muldraugh, KY,\r\n}\r\n",
            Text());
    }

    [Fact]
    public async Task SetModIds_LfFileWithBraceOnTheModsLine_KeepsLfAndReadsTheEntries()
    {
        Write("VERSION = 1,\nmods {\n  mod=A ,\n}\nmaps\n{\n}");
        var file = await ReadAsync();

        Assert.Equal(["A"], file.ModIds);
        file.SetModIds([]);
        ResultAssert.Ok(await file.WriteAsync(Ct));

        Assert.Equal("VERSION = 1,\nmods\n{\n}\nmaps\n{\n}", Text());
    }

    [Fact]
    public async Task SetModIds_MissingFile_WritesTheGameShapeWithTheGivenNewline()
    {
        var file = await ReadAsync("\n");
        file.SetModIds(["A"]);
        ResultAssert.Ok(await file.WriteAsync(Ct));

        Assert.Equal("VERSION = 1,\n\nmods\n{\n    mod = A,\n}\n\nmaps\n{\n}\n", Text());
        Assert.False(File.Exists(FilePath + ".pzlauncher.bak"));
        Assert.False(File.Exists(FilePath + ".pzlauncher.orig"));
    }

    [Fact]
    public async Task SetModIds_NoModsBlock_InsertsOneBeforeMaps()
    {
        Write("VERSION = 1,\r\n\r\nmaps\r\n{\r\n}\r\n");

        await ApplyAsync("A");

        Assert.Equal("VERSION = 1,\r\n\r\nmods\r\n{\r\n    mod = A,\r\n}\r\n\r\nmaps\r\n{\r\n}\r\n", Text());
    }

    [Fact]
    public async Task ReadAsync_UnclosedModsBlock_IsMalformed()
    {
        Write("VERSION = 1,\r\nmods\r\n{\r\n    mod = A,\r\n");

        ResultAssert.Failed(await ActiveModsFile.ReadAsync(_cache, "\r\n", Ct), ErrorCode.ModCollectionFileMalformed);
    }

    [Fact]
    public async Task WriteAsync_TwoApplies_RollsTheBackupAndKeepsTheFirstOriginalForever()
    {
        Write(GameFile);

        await ApplyAsync("B");
        var afterFirst = Text();
        await ApplyAsync("C");

        Assert.Equal(GameFile, await File.ReadAllTextAsync(FilePath + ".pzlauncher.orig", Ct));
        Assert.Equal(afterFirst, await File.ReadAllTextAsync(FilePath + ".pzlauncher.bak", Ct));
    }

    [Fact]
    public async Task WriteAsync_Unchanged_WritesNothing()
    {
        Write(GameFile);

        await ApplyAsync("ZombieBuddy", "ModA");

        Assert.Equal(GameFile, Text());
        Assert.False(File.Exists(FilePath + ".pzlauncher.bak"));
        Assert.False(File.Exists(FilePath + ".pzlauncher.orig"));
    }
}
