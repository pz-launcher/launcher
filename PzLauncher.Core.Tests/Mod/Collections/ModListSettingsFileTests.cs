using System.Text;
using PzLauncher.Core.Common;
using PzLauncher.Core.Mod.Collections;
using PzLauncher.Core.Tests.TestSupport;

namespace PzLauncher.Core.Tests.Mod.Collections;

public sealed class ModListSettingsFileTests : IDisposable
{
    // As the game writes it: favourites first, then one line per collection, "\n" after each.
    private const string GameFile = "!fav!:FavA;FavB;\nB42 pack:ModA;ModB;ModC;\nmy pack:ModB;\n";

    private readonly TempDirectory _temp = new();
    private readonly string _cache;

    public ModListSettingsFileTests()
    {
        _cache = _temp.CreateDirectory("Zomboid");
    }

    public void Dispose() => _temp.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string FilePath => Path.Combine(_cache, "Lua", "pz_modlist_settings.cfg");

    private void Write(string text) => WriteBytes(Encoding.UTF8.GetBytes(text));

    private void WriteBytes(byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllBytes(FilePath, bytes);
    }

    private string Text() => File.ReadAllText(FilePath);

    private async Task<ModListSettingsFile> ReadAsync() => ResultAssert.Ok(await ModListSettingsFile.ReadAsync(_cache, Ct));

    private async Task EditAsync(Action<ModListSettingsFile> edit)
    {
        var file = await ReadAsync();
        edit(file);
        ResultAssert.Ok(await file.WriteAsync(Ct));
    }

    [Fact]
    public async Task Collections_GameFile_SkipsFavouritesAndKeepsLineOrder()
    {
        Write(GameFile);

        var collections = (await ReadAsync()).Collections;

        Assert.Equal(["B42 pack", "my pack"], collections.Select(c => c.Name));
        Assert.Equal(["ModA", "ModB", "ModC"], collections[0].ModIds);
    }

    [Fact]
    public async Task Collections_CrlfEmptyNameNoSeparatorAndDuplicates_ReadLikeTheGame()
    {
        Write("first:A;\r\n:Orphan;\r\nno separator\r\n\r\nfirst:B;C;;B;\r\nsecond:\r\n");

        var collections = (await ReadAsync()).Collections;

        Assert.Equal(["first", "second"], collections.Select(c => c.Name));
        Assert.Equal(["B", "C"], collections[0].ModIds);
        Assert.Empty(collections[1].ModIds);
    }

    [Fact]
    public async Task Collections_FavouritesPrefixAfterTheFirstLine_IsACollection()
    {
        Write("pack:A;\n!fav!:B;\n");

        Assert.Equal(["pack", "!fav!"], (await ReadAsync()).Collections.Select(c => c.Name));
    }

    [Fact]
    public async Task Collections_MissingFile_IsEmpty() =>
        Assert.Empty((await ReadAsync()).Collections);

    [Fact]
    public async Task Save_ExistingName_ReplacesItsLineInPlaceAndKeepsTheOthersByteForByte()
    {
        Write(GameFile);

        await EditAsync(file => Assert.True(file.Save(null, "B42 pack", ["ModC", "ModA"])));

        Assert.Equal("!fav!:FavA;FavB;\nB42 pack:ModC;ModA;\nmy pack:ModB;\n", Text());
    }

    [Fact]
    public async Task Save_NewName_AppendsAGameShapedLine()
    {
        Write(GameFile);

        await EditAsync(file => file.Save(null, "new", ["X", "Y"]));

        Assert.Equal(GameFile + "new:X;Y;\n", Text());
    }

    [Fact]
    public async Task Save_FileWithoutTrailingNewlineInCrlf_TerminatesTheLastLineWithCrlf()
    {
        Write("a:A;\r\nb:B;");

        await EditAsync(file => file.Save(null, "c", ["C"]));

        Assert.Equal("a:A;\r\nb:B;\r\nc:C;\r\n", Text());
    }

    [Fact]
    public async Task Save_MissingFile_CreatesItWithLf()
    {
        await EditAsync(file => file.Save(null, "new", ["X"]));

        Assert.Equal("new:X;\n", Text());
        Assert.False(File.Exists(FilePath + ".pzlauncher.bak"));
    }

    [Fact]
    public async Task Save_DuplicateLines_RewritesTheFirstAndRemovesTheRest()
    {
        Write("dup:A;\nother:O;\ndup:B;\n");

        await EditAsync(file => file.Save(null, "dup", ["C"]));

        Assert.Equal("dup:C;\nother:O;\n", Text());
    }

    [Fact]
    public async Task Save_Rename_KeepsThePositionAndDropsAnOverwrittenCollection()
    {
        Write("!fav!:\ntarget:T;\nold:A;\nkeep:K;\n");

        await EditAsync(file => Assert.True(file.Save("old", "target", ["A"])));

        Assert.Equal("!fav!:\ntarget:A;\nkeep:K;\n", Text());
    }

    [Fact]
    public async Task Save_RenameOfMissingCollection_ReturnsFalse()
    {
        Write(GameFile);

        var file = await ReadAsync();

        Assert.False(file.Save("gone", "new", ["A"]));
    }

    [Fact]
    public async Task Delete_RemovesEveryLineOfThatName()
    {
        Write("a:A;\nb:B;\na:C;\n");

        await EditAsync(file => Assert.True(file.Delete("a")));

        Assert.Equal("b:B;\n", Text());
    }

    [Fact]
    public async Task Delete_MissingCollection_ReturnsFalseAndWritesNothing()
    {
        Write(GameFile);
        var file = await ReadAsync();

        Assert.False(file.Delete("gone"));
        ResultAssert.Ok(await file.WriteAsync(Ct));

        Assert.False(File.Exists(FilePath + ".pzlauncher.bak"));
    }

    [Fact]
    public async Task WriteAsync_Changed_KeepsThePreviousFileAsTheRollingBackup()
    {
        Write(GameFile);

        await EditAsync(file => file.Save(null, "first", ["A"]));
        var afterFirst = Text();
        await EditAsync(file => file.Save(null, "second", ["B"]));

        Assert.Equal(afterFirst, await File.ReadAllTextAsync(FilePath + ".pzlauncher.bak", Ct));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(FilePath)!, "*.tmp"));
    }

    [Fact]
    public async Task WriteAsync_BackupNotWritable_FailsAndLeavesTheFileUntouched()
    {
        Write(GameFile);
        Directory.CreateDirectory(FilePath + ".pzlauncher.bak");
        var file = await ReadAsync();
        file.Save(null, "new", ["A"]);

        var error = ResultAssert.Failed(await file.WriteAsync(Ct), ErrorCode.ModCollectionFileWriteFailed);

        Assert.Equal(FilePath, error.Details["path"]);
        Assert.Equal(GameFile, Text());
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(FilePath)!, "*.tmp"));
    }

    [Fact]
    public async Task WriteAsync_BomAndNonAsciiNames_SurviveByteForByte()
    {
        byte[] original = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("!fav!:\nB42·20通用合集:A;\n")];
        byte[] expected = [.. original, .. Encoding.UTF8.GetBytes("my合集:B;\n")];
        WriteBytes(original);

        await EditAsync(file => file.Save(null, "my合集", ["B"]));

        Assert.Equal(expected, await File.ReadAllBytesAsync(FilePath, Ct));
    }

    [Fact]
    public async Task ReadAsync_InvalidUtf8_IsMalformed()
    {
        WriteBytes([.. Encoding.UTF8.GetBytes("a:"), 0xFF, 0xFE, (byte)'\n']);

        ResultAssert.Failed(await ModListSettingsFile.ReadAsync(_cache, Ct), ErrorCode.ModCollectionFileMalformed);
    }

    [Fact]
    public async Task ReadAsync_Locked_IsUnreadable()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows refuses to read a file opened without sharing.");
        Write(GameFile);
        using var locked = File.Open(FilePath, FileMode.Open, FileAccess.Read, FileShare.None);

        var error = ResultAssert.Failed(await ModListSettingsFile.ReadAsync(_cache, Ct), ErrorCode.ModCollectionFileUnreadable);

        Assert.Equal(FilePath, error.Details["path"]);
    }
}
