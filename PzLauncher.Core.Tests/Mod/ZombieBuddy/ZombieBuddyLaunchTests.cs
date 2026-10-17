using Microsoft.Extensions.Logging.Abstractions;
using PzLauncher.Core.Common;
using PzLauncher.Core.Mod.ZombieBuddy;
using PzLauncher.Core.Platform;
using PzLauncher.Core.Steam;
using PzLauncher.Core.Tests.TestSupport;

namespace PzLauncher.Core.Tests.Mod.ZombieBuddy;

public sealed class ZombieBuddyLaunchTests : IDisposable
{
    private const string AllowAll = "-agentlib:zbNative=policy=allow-all";

    private readonly TempDirectory _temp = new();
    private readonly string _library;
    private readonly string _home;
    private readonly FakePlatformEnvironment _platform;

    public ZombieBuddyLaunchTests()
    {
        var steam = new SteamLayoutBuilder(_temp.Path);
        _library = steam.CreateLibrary("Library");
        _home = _temp.CreateDirectory("home");
        _platform = FakePlatformEnvironment.WithRegistrySteam(steam.CreateSteamRoot(_library));
        _platform.HomeDirectory = _home;
    }

    public void Dispose() => _temp.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private GameInstallBuilder Installs => new(Path.Combine(_library, "steamapps", "common"), PlatformKind.Windows);

    private string CreateGame() => Installs.CreateFlat("ProjectZomboid", GameInstallBuilder.WindowsGameJsonWith(AllowAll), withJre: false);

    private string SteamZabric(string? manifest = ZabricBuilder.PreloadManifest) =>
        ZabricBuilder.Create(Path.Combine(WorkshopLibraries.ItemFolder(_library, "3820726385"), "mods", "Zabric"), manifest: manifest);

    private Task<ZombieBuddyLaunch?> ResolveAsync(string root) =>
        ZombieBuddyLaunch.ResolveAsync(
            ZombieBuddyServiceTests.CreateService(_platform), ZombieBuddyServiceTests.CreateWorkshopLibraries(_platform), _platform, root, "", Ct);

    private Task<ZombieBuddyLaunch> LaunchAsync(string root, string extraGameArgs = "") =>
        LaunchAsync(ZombieBuddyServiceTests.CreateService(_platform), _platform, root, extraGameArgs);

    internal static async Task<ZombieBuddyLaunch> LaunchAsync(
        IZombieBuddyService zombieBuddy, FakePlatformEnvironment platform, string root, string extraGameArgs = "")
    {
        var launch = await ZombieBuddyLaunch.ResolveAsync(
            zombieBuddy, ZombieBuddyServiceTests.CreateWorkshopLibraries(platform), platform, root, extraGameArgs, TestContext.Current.CancellationToken);
        Assert.NotNull(launch);
        return launch;
    }

    private static Task<Result<ZombieBuddyPreloadEntry>?> FindZabricAsync(ZombieBuddyLaunch launch) =>
        launch.FindPreloadableAsync("Zabric", "3820726385", NullLogger.Instance, Ct);

    [Fact]
    public async Task ResolveAsync_Game_ResolvesStatusRootAndDefaultCache()
    {
        var root = CreateGame();

        var launch = await LaunchAsync(root);

        Assert.Equal(root, launch.Root);
        Assert.True(launch.Status.IsAllowAll);
        Assert.Equal(Path.Combine(_home, ".zombie_buddy"), launch.Status.ConfigFolder);
        Assert.Equal(Path.Combine(_home, "Zomboid"), launch.Cache);
        Assert.False(launch.IsPreloaded("Zabric"));
    }

    [Fact]
    public async Task ResolveAsync_CacheDirArg_UsesIt()
    {
        var cache = Path.Combine(_temp.Path, "pz cache");

        var launch = await LaunchAsync(CreateGame(), $"-cachedir=\"{cache}\" -debug");

        Assert.Equal(cache, launch.Cache);
    }

    [Fact]
    public async Task ResolveAsync_CacheUnresolvable_ResolvesWithoutCache()
    {
        var root = CreateGame();
        _platform.HomeDirectory = "";

        Assert.Null((await LaunchAsync(root)).Cache);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ResolveAsync_ServerOrMacOS_IsNull(bool macOS)
    {
        var root = macOS ? CreateGame() : Installs.CreateServer("Server");
        if (macOS)
        {
            _platform.Os = PlatformKind.MacOS;
        }

        Assert.Null(await ResolveAsync(root));
    }

    [Fact]
    public async Task ResolveAsync_GameJsonMissing_IsNull()
    {
        var root = Installs.CreateFlat("ProjectZomboid", json: null, withJre: false);

        Assert.Null(await ResolveAsync(root));
    }

    [Fact]
    public async Task ResolveAsync_RelativeRoot_IsNull() => Assert.Null(await ResolveAsync("ProjectZomboid"));

    [Fact]
    public async Task LibrariesAsync_CalledTwice_ReadsOnce()
    {
        var launch = await LaunchAsync(CreateGame());

        var first = await launch.LibrariesAsync(Ct);

        Assert.Equal(_library, first[0]);
        Assert.Same(first, await launch.LibrariesAsync(Ct));
    }

    [Fact]
    public async Task FindPreloadableAsync_NoCopy_IsNull() => Assert.Null(await FindZabricAsync(await LaunchAsync(CreateGame())));

    [Fact]
    public async Task FindPreloadableAsync_CopyNotPreloadable_ReturnsTheReason()
    {
        var root = CreateGame();
        SteamZabric(manifest: "Manifest-Version: 1.0\r\n\r\n");

        var found = Assert.NotNull(await FindZabricAsync(await LaunchAsync(root)));

        var error = ResultAssert.Failed(found, ErrorCode.ZombieBuddyModNotPreloadable);
        Assert.Equal(ZombieBuddyJavaMod.NoManifestPreload, error.Details["reason"]);
    }

    [Fact]
    public async Task FindPreloadableAsync_PreloadableCopy_ReturnsItsEntry()
    {
        var root = CreateGame();
        var zabric = SteamZabric();

        var found = Assert.NotNull(await FindZabricAsync(await LaunchAsync(root)));

        Assert.Equal(new ZombieBuddyPreloadEntry(ZabricBuilder.InfPath(zabric), ZabricBuilder.JarPath(zabric)), ResultAssert.Ok(found));
    }
}
