using PzLauncher.Core.Common;
using PzLauncher.Core.Steam;
using PzLauncher.Core.Tests.TestSupport;

namespace PzLauncher.Core.Tests.Steam;

public sealed class SteamLibraryReaderTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly SteamLibraryReader _reader = new();
    private readonly string _steamRoot;
    private readonly string _manifestPath;

    public SteamLibraryReaderTests()
    {
        _steamRoot = _temp.CreateDirectory("Steam");
        _manifestPath = Path.Combine(_temp.Path, "appmanifest_1.acf");
    }

    public void Dispose() => _temp.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private Task<Result<IReadOnlyList<string>>> ReadLibraryPathsAsync(string? content)
    {
        if (content is not null)
        {
            SteamLayoutBuilder.WriteRaw(SteamLibraryReader.LibraryFoldersPath(_steamRoot), content);
        }

        return _reader.ReadLibraryPathsAsync(_steamRoot, Ct);
    }

    private Task<Result<SteamAppManifest>> ReadManifestAsync(string content)
    {
        SteamLayoutBuilder.WriteRaw(_manifestPath, content);
        return _reader.ReadManifestAsync(_manifestPath, Ct);
    }

    [Fact]
    public async Task ReadLibraryPathsAsync_RealFile_ReturnsPathsInOrder()
    {
        var paths = ResultAssert.Ok(await ReadLibraryPathsAsync(Fixtures.Read("Steam", "libraryfolders.vdf")));

        Assert.Equal([@"C:\Program Files (x86)\Steam", @"E:\SteamLibrary"], paths);
    }

    [Fact]
    public async Task ReadLibraryPathsAsync_LegacyFormat_ReadsPlainValues()
    {
        var lib = Path.Combine(_temp.Path, "Lib");

        var paths = ResultAssert.Ok(await ReadLibraryPathsAsync(
            $"\"LibraryFolders\" {{ \"TimeNextStatsReport\" \"1700000000\" \"ContentStatsID\" \"-123\" \"1\" \"{lib.Replace("\\", "\\\\", StringComparison.Ordinal)}\" }}"));

        Assert.Equal([lib], paths);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("\"something\" { }")]
    public async Task ReadLibraryPathsAsync_FileMissingOrNoLibraryFoldersBlock_ReturnsLibraryUnreadable(string? content)
    {
        var error = ResultAssert.Failed(await ReadLibraryPathsAsync(content), ErrorCode.SteamLibraryUnreadable);

        Assert.Equal(SteamLibraryReader.LibraryFoldersPath(_steamRoot), error.Details["path"]);
    }

    [Fact]
    public async Task ReadManifestAsync_RealFile_ReadsFields()
    {
        var result = await _reader.ReadManifestAsync(Fixtures.Path("Steam", "appmanifest_108600.acf"), Ct);

        Assert.Equal(new SteamAppManifest("ProjectZomboid", "25485521", null), ResultAssert.Ok(result));
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
        var result = await ReadManifestAsync($"\"AppState\" {{ \"installdir\" \"Game\" \"buildid\" \"7\" {configBlocks} }}");

        Assert.Equal(expectedBranch, ResultAssert.Ok(result).BetaBranch);
    }

    [Theory]
    [InlineData("\"AppState\" { \"buildid\" \"7\" }")]
    [InlineData("\"AppState\" { \"installdir\" \"Game\" }")]
    [InlineData("\"AppState\" { \"installdir\" \"Game\" \"buildid\" \"\" }")]
    [InlineData("\"AppState\" { \"installdir\" \"\" }")]
    [InlineData("\"AppState\" { \"installdir\" \"Pro\0ject\" \"buildid\" \"7\" }")]
    [InlineData("\"Other\" { \"installdir\" \"Game\" }")]
    [InlineData("\"AppState\" { \"installdir\" ")]
    public async Task ReadManifestAsync_InvalidContent_ReturnsManifestUnreadableWithPathAndReason(string content)
    {
        var error = ResultAssert.Failed(await ReadManifestAsync(content), ErrorCode.SteamManifestUnreadable);

        Assert.Equal(_manifestPath, error.Details["path"]);
        Assert.False(string.IsNullOrWhiteSpace(error.Details["reason"]));
    }
}
