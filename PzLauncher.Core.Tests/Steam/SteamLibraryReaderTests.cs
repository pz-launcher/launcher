using PzLauncher.Core.Common;
using PzLauncher.Core.Steam;
using PzLauncher.Core.Tests.TestSupport;

namespace PzLauncher.Core.Tests.Steam;

public sealed class SteamLibraryReaderTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly SteamLibraryReader _reader = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task ReadLibraryPathsAsync_RealFile_ReturnsPathsInOrder()
    {
        var root = _temp.CreateDirectory("Steam");
        File.Copy(Fixtures.Path("Steam", "libraryfolders.vdf"), SteamLibraryReader.LibraryFoldersPath(EnsureSteamapps(root)));

        var result = await _reader.ReadLibraryPathsAsync(root, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal([@"C:\Program Files (x86)\Steam", @"E:\SteamLibrary"], result.Value);
    }

    [Fact]
    public async Task ReadLibraryPathsAsync_LegacyFormat_ReadsPlainValues()
    {
        var root = _temp.CreateDirectory("Steam");
        var lib = Path.Combine(_temp.Path, "Lib").Replace("\\", "\\\\", StringComparison.Ordinal);
        SteamLayoutBuilder.WriteRaw(
            SteamLibraryReader.LibraryFoldersPath(root),
            $"\"LibraryFolders\"\n{{\n\t\"TimeNextStatsReport\"\t\t\"1700000000\"\n\t\"ContentStatsID\"\t\t\"-123\"\n\t\"1\"\t\t\"{lib}\"\n}}\n");

        var result = await _reader.ReadLibraryPathsAsync(root, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal([Path.Combine(_temp.Path, "Lib")], result.Value);
    }

    [Fact]
    public async Task ReadLibraryPathsAsync_FileMissing_ReturnsLibraryUnreadable()
    {
        var root = _temp.CreateDirectory("Steam");

        var result = await _reader.ReadLibraryPathsAsync(root, TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.SteamLibraryUnreadable, result.Error!.Code);
        Assert.Equal(ErrorKind.External, result.Error.Kind);
        Assert.Equal(SteamLibraryReader.LibraryFoldersPath(root), result.Error.Details["path"]);
    }

    [Fact]
    public async Task ReadLibraryPathsAsync_NoLibraryFoldersBlock_ReturnsLibraryUnreadable()
    {
        var root = _temp.CreateDirectory("Steam");
        SteamLayoutBuilder.WriteRaw(SteamLibraryReader.LibraryFoldersPath(root), "\"something\" { }");

        var result = await _reader.ReadLibraryPathsAsync(root, TestContext.Current.CancellationToken);

        Assert.Equal(ErrorCode.SteamLibraryUnreadable, result.Error!.Code);
    }

    [Fact]
    public async Task ReadManifestAsync_RealFile_ReadsFields()
    {
        var path = Fixtures.Path("Steam", "appmanifest_108600.acf");

        var result = await _reader.ReadManifestAsync(path, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(new SteamAppManifest("ProjectZomboid", "25485521", null), result.Value);
    }

    [Theory]
    [InlineData("\"UserConfig\" { \"BetaKey\" \"unstable\" }", "unstable")]
    [InlineData("\"UserConfig\" { \"betakey\" \"b41\" }", "b41")]
    [InlineData("\"MountedConfig\" { \"BetaKey\" \"mounted\" }", "mounted")]
    [InlineData("\"UserConfig\" { \"BetaKey\" \"user\" } \"MountedConfig\" { \"BetaKey\" \"mounted\" }", "user")]
    [InlineData("\"UserConfig\" { \"BetaKey\" \"public\" }", null)]
    [InlineData("\"UserConfig\" { \"BetaKey\" \"\" }", null)]
    [InlineData("", null)]
    public async Task ReadManifestAsync_BetaKey_ResolvesBranch(string configBlocks, string? expectedBranch)
    {
        var path = Path.Combine(_temp.Path, "appmanifest_1.acf");
        SteamLayoutBuilder.WriteRaw(path, $"\"AppState\" {{ \"installdir\" \"Game\" \"buildid\" \"7\" {configBlocks} }}");

        var result = await _reader.ReadManifestAsync(path, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(expectedBranch, result.Value.BetaBranch);
    }

    [Fact]
    public async Task ReadManifestAsync_NumberLikeInstallDirAndBetaKey_ReturnsNormalizedNumbers()
    {
        // Known limitation (design.md "VDF parsing"); pinned so a library upgrade that changes it is noticed.
        var path = Path.Combine(_temp.Path, "appmanifest_1.acf");
        SteamLayoutBuilder.WriteRaw(path, "\"AppState\" { \"installdir\" \"007\" \"buildid\" \"0012\" \"UserConfig\" { \"BetaKey\" \"42.0\" } }");

        var result = await _reader.ReadManifestAsync(path, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal("7", result.Value.InstallDir);
        Assert.Equal("12", result.Value.BuildId);
        Assert.Equal("42", result.Value.BetaBranch);
    }

    [Theory]
    [InlineData("\"AppState\" { \"buildid\" \"7\" }")]
    [InlineData("\"AppState\" { \"installdir\" \"Game\" }")]
    [InlineData("\"AppState\" { \"installdir\" \"Game\" \"buildid\" \"\" }")]
    [InlineData("\"AppState\" { \"installdir\" \"\" }")]
    [InlineData("\"Other\" { \"installdir\" \"Game\" }")]
    [InlineData("\"AppState\" { \"installdir\" ")]
    public async Task ReadManifestAsync_InvalidContent_ReturnsManifestUnreadable(string content)
    {
        var path = Path.Combine(_temp.Path, "appmanifest_1.acf");
        SteamLayoutBuilder.WriteRaw(path, content);

        var result = await _reader.ReadManifestAsync(path, TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.SteamManifestUnreadable, result.Error!.Code);
        Assert.Equal(ErrorKind.External, result.Error.Kind);
        Assert.Equal(path, result.Error.Details["path"]);
    }

    [Fact]
    public async Task ReadManifestAsync_BuildIdMissing_ReturnsManifestUnreadableWithReason()
    {
        var path = Path.Combine(_temp.Path, "appmanifest_1.acf");
        SteamLayoutBuilder.WriteRaw(path, "\"AppState\" { \"installdir\" \"Game\" }");

        var result = await _reader.ReadManifestAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(ErrorCode.SteamManifestUnreadable, result.Error!.Code);
        Assert.Equal(ErrorKind.External, result.Error.Kind);
        Assert.Equal("missing \"buildid\"", result.Error.Details["reason"]);
    }

    [Fact]
    public async Task ReadManifestAsync_MalformedVdf_ReasonComesFromParser()
    {
        var path = Path.Combine(_temp.Path, "appmanifest_1.acf");
        SteamLayoutBuilder.WriteRaw(path, "\"AppState\" { \"installdir\" ");

        var result = await _reader.ReadManifestAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(ErrorCode.SteamManifestUnreadable, result.Error!.Code);
        Assert.False(string.IsNullOrWhiteSpace(result.Error.Details["reason"]));
    }

    [Fact]
    public async Task ReadManifestAsync_InstallDirWithNulChar_ReturnsManifestUnreadable()
    {
        var path = Path.Combine(_temp.Path, "appmanifest_1.acf");
        SteamLayoutBuilder.WriteRaw(path, "\"AppState\" { \"installdir\" \"Pro\0ject\" }");

        var result = await _reader.ReadManifestAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(ErrorCode.SteamManifestUnreadable, result.Error!.Code);
        Assert.Equal(ErrorKind.External, result.Error.Kind);
    }

    private static string EnsureSteamapps(string root)
    {
        Directory.CreateDirectory(Path.Combine(root, "steamapps"));
        return root;
    }
}
