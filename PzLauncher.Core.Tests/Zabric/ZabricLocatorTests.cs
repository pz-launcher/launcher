using Microsoft.Extensions.Logging.Abstractions;
using PzLauncher.Core.Tests.TestSupport;
using PzLauncher.Core.Zabric;

namespace PzLauncher.Core.Tests.Zabric;

public sealed class ZabricLocatorTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly string _cache;
    private readonly string _library;

    public ZabricLocatorTests()
    {
        _cache = _temp.CreateDirectory("Zomboid");
        _library = _temp.CreateDirectory("Library");
    }

    public void Dispose() => _temp.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string Staged(string item = "Zabric") => Path.Combine(_cache, "Workshop", item, "Contents", "mods", "Zabric");

    private string Steam(string item = "3820726385", string? library = null) =>
        Path.Combine(library ?? _library, "steamapps", "workshop", "content", "108600", item, "mods", "Zabric");

    private string Local => Path.Combine(_cache, "mods", "Zabric");

    private async Task<string?> FindInfAsync(string? cache = null, params string[] libraries)
    {
        var found = await ZabricLocator.FindAsync(cache ?? _cache, libraries.Length == 0 ? [_library] : libraries, NullLogger.Instance, Ct);
        return found?.InfPath;
    }

    [Fact]
    public void CacheFolder_NoCacheDirArg_IsZomboidInHome()
    {
        var home = _temp.CreateDirectory("home");

        Assert.Equal(Path.Combine(home, "Zomboid"), ZabricLocator.CacheFolder("-debug", _temp.Path, home));
    }

    [Fact]
    public void CacheFolder_SeveralQuotedCacheDirArgs_LastWins()
    {
        var first = Path.Combine(_temp.Path, "first");
        var last = Path.Combine(_temp.Path, "pz cache");

        Assert.Equal(last, ZabricLocator.CacheFolder($"-cachedir={first} -debug -cachedir=\"{last}\"", _temp.Path, ""));
    }

    [Fact]
    public void CacheFolder_RelativeCacheDir_ResolvesAgainstTheGameFolder() =>
        Assert.Equal(Path.Combine(_temp.Path, "cache"), ZabricLocator.CacheFolder("-cachedir=cache", _temp.Path, ""));

    [Fact]
    public void CacheFolder_NoHome_IsNull() =>
        Assert.Null(ZabricLocator.CacheFolder("", _temp.Path, ""));

    [Fact]
    public async Task FindAsync_CopiesEverywhere_TakesTheStagedWorkshopCopy()
    {
        var staged = ZabricBuilder.Create(Staged());
        ZabricBuilder.Create(Steam());
        ZabricBuilder.Create(Local);

        Assert.Equal(ZabricBuilder.InfPath(staged), await FindInfAsync());
    }

    [Fact]
    public async Task FindAsync_SteamAndLocalCopies_TakesTheSteamCopy()
    {
        var steam = ZabricBuilder.Create(Steam());
        ZabricBuilder.Create(Local);

        Assert.Equal(ZabricBuilder.InfPath(steam), await FindInfAsync());
    }

    [Fact]
    public async Task FindAsync_OnlyLocalCopy_TakesIt()
    {
        var local = ZabricBuilder.Create(Local);

        Assert.Equal(ZabricBuilder.InfPath(local), await FindInfAsync());
    }

    [Fact]
    public async Task FindAsync_NoCache_SearchesSteamOnly()
    {
        ZabricBuilder.Create(Local);

        Assert.Null(await ZabricLocator.FindAsync(null, [_library], NullLogger.Instance, Ct));
    }

    [Fact]
    public async Task FindAsync_SeveralSteamItems_TakesZabricsItemFirst()
    {
        ZabricBuilder.Create(Steam("1000"));
        var own = ZabricBuilder.Create(Steam());

        Assert.Equal(ZabricBuilder.InfPath(own), await FindInfAsync());
    }

    [Fact]
    public async Task FindAsync_SeveralLibraries_TakesTheFirstLibrarysCopy()
    {
        var other = _temp.CreateDirectory("Other");
        ZabricBuilder.Create(Steam(library: other));
        var first = ZabricBuilder.Create(Steam("1000"));

        Assert.Equal(ZabricBuilder.InfPath(first), await FindInfAsync(null, _library, other));
    }

    [Fact]
    public async Task FindAsync_SeveralVersionFolders_TakesTheHighestHoldingModInfo()
    {
        ZabricBuilder.Create(Local, "42");
        var highest = ZabricBuilder.Create(Local, "42.13");
        ZabricBuilder.Create(Local, "42.2");
        _temp.CreateDirectory("Zomboid", "mods", "Zabric", "43");

        Assert.Equal(ZabricBuilder.InfPath(highest), await FindInfAsync());
    }

    [Fact]
    public async Task FindAsync_FirstCopyHasAnotherId_SkipsIt()
    {
        ZabricBuilder.Create(Staged(), modInfo: ZabricBuilder.ModInfo(id: "NotZabric"));
        var steam = ZabricBuilder.Create(Steam());

        Assert.Equal(ZabricBuilder.InfPath(steam), await FindInfAsync());
    }

    [Fact]
    public async Task FindAsync_NoCopy_ReturnsNull()
    {
        _temp.CreateDirectory("Zomboid", "Workshop", "Other", "Contents", "mods", "Other");

        Assert.Null(await FindInfAsync());
    }

    [Theory]
    [InlineData("42", new[] { 42 })]
    [InlineData("42.13", new[] { 42, 13 })]
    [InlineData("common", null)]
    [InlineData("42.", null)]
    [InlineData("-42", null)]
    public void ParseVersion_FolderName_ParsesDottedNumbers(string name, int[]? expected) =>
        Assert.Equal(expected, ZabricLocator.ParseVersion(name));
}
