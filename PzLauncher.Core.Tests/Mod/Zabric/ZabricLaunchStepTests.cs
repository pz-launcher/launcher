using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using PzLauncher.Core.Common;
using PzLauncher.Core.Mod.Zabric;
using PzLauncher.Core.Mod.ZombieBuddy;
using PzLauncher.Core.Platform;
using PzLauncher.Core.Steam;
using PzLauncher.Core.Tests.Mod.ZombieBuddy;
using PzLauncher.Core.Tests.TestSupport;

namespace PzLauncher.Core.Tests.Mod.Zabric;

public sealed class ZabricLaunchStepTests : IDisposable
{
    private const string AllowAll = "-agentlib:zbNative=policy=allow-all";

    private readonly TempDirectory _temp = new();
    private readonly string _library;
    private readonly string _home;
    private readonly FakePlatformEnvironment _platform;
    private readonly ZombieBuddyService _zombieBuddy;
    private readonly ZabricLaunchStep _step;

    public ZabricLaunchStepTests()
    {
        var steam = new SteamLayoutBuilder(_temp.Path);
        _library = steam.CreateLibrary("Library");
        _home = _temp.CreateDirectory("home");
        _platform = FakePlatformEnvironment.WithRegistrySteam(steam.CreateSteamRoot(_library));
        _platform.HomeDirectory = _home;
        _zombieBuddy = ZombieBuddyServiceTests.CreateService(_platform);
        _step = new ZabricLaunchStep(_platform, NullLogger<ZabricLaunchStep>.Instance);
    }

    public void Dispose() => _temp.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string DefaultConfig => Path.Combine(_home, ".zombie_buddy", "config.json");

    private GameInstallBuilder Installs => new(Path.Combine(_library, "steamapps", "common"), PlatformKind.Windows);

    private string CreateGame(string agentArg = AllowAll) =>
        Installs.CreateFlat("ProjectZomboid", GameInstallBuilder.WindowsGameJsonWith(agentArg), withJre: false);

    private string SteamZabric(string? modInfo = null) =>
        ZabricBuilder.Create(Path.Combine(WorkshopLibraries.ItemFolder(_library, "3820726385"), "mods", "Zabric"), modInfo: modInfo);

    private static string StagedZabric(string cache, string classes = "v1") =>
        ZabricBuilder.Create(Path.Combine(cache, "Workshop", "Zabric", "Contents", "mods", "Zabric"), classes: classes);

    private Task<ZombieBuddyLaunch> LaunchAsync(string root, string extraGameArgs = "") =>
        ZombieBuddyLaunchTests.LaunchAsync(_zombieBuddy, _platform, root, extraGameArgs);

    // Whether later steps see Zabric preloaded.
    private async Task<bool> RunAsync(string root, string extraGameArgs = "")
    {
        var launch = await LaunchAsync(root, extraGameArgs);
        ResultAssert.Ok(await _step.RunAsync(launch, Ct));
        return launch.IsPreloaded("Zabric");
    }

    private static (string? Inf, string? Jar) StoredZabric(string config)
    {
        var entry = JsonNode.Parse(File.ReadAllText(config))?["preload_mods"]?["Zabric"];
        return (entry?["infPath"]?.GetValue<string>(), entry?["jarPath"]?.GetValue<string>());
    }

    [Fact]
    public async Task RunAsync_AllowAllWithSteamCopy_WritesTheEntryThenKeepsIt()
    {
        var root = CreateGame();
        var zabric = SteamZabric();

        Assert.True(await RunAsync(root));
        Assert.Equal((ZabricBuilder.InfPath(zabric), ZabricBuilder.JarPath(zabric)), StoredZabric(DefaultConfig));

        var config = await File.ReadAllTextAsync(DefaultConfig, Ct);
        Assert.True(await RunAsync(root));
        Assert.Equal(config, await File.ReadAllTextAsync(DefaultConfig, Ct));
    }

    [Theory]
    [InlineData("-agentlib:zbNative")]
    [InlineData("-agentlib:zbNative=policy=prompt")]
    [InlineData("-javaagent:ZombieBuddy.jar=policy=allow-all")]
    public async Task RunAsync_NotAllowAll_WritesNothing(string agentArg)
    {
        var root = CreateGame(agentArg);
        SteamZabric();

        Assert.False(await RunAsync(root));
        Assert.False(File.Exists(DefaultConfig));
    }

    [Fact]
    public async Task RunAsync_NoZabric_WritesNothing()
    {
        var root = CreateGame();
        SteamZabric(ZabricBuilder.ModInfo(id: "Other"));

        Assert.False(await RunAsync(root));
        Assert.False(File.Exists(DefaultConfig));
    }

    [Fact]
    public async Task RunAsync_StagedCopies_UsesTheOneInTheCacheDirOfTheGameArgs()
    {
        var root = CreateGame();
        SteamZabric();
        var inHome = StagedZabric(Path.Combine(_home, "Zomboid"));
        var cache = Path.Combine(_temp.Path, "pz cache");
        var inCache = StagedZabric(cache, classes: "v2");

        Assert.True(await RunAsync(root, "-debug"));
        Assert.Equal(ZabricBuilder.JarPath(inHome), StoredZabric(DefaultConfig).Jar);

        Assert.True(await RunAsync(root, $"-cachedir=\"{cache}\" -debug"));
        Assert.Equal(ZabricBuilder.JarPath(inCache), StoredZabric(DefaultConfig).Jar);
    }

    [Fact]
    public async Task RunAsync_ConfigDirOption_WritesThere()
    {
        var configDir = Path.Combine(_temp.Path, "zb");
        var root = CreateGame($"-agentlib:zbNative=config_dir={configDir},policy=allow-all");
        SteamZabric();

        Assert.True(await RunAsync(root));

        Assert.True(File.Exists(Path.Combine(configDir, "config.json")));
        Assert.False(File.Exists(DefaultConfig));
    }

    [Fact]
    public async Task RunAsync_CacheUnresolved_UsesTheSteamCopy()
    {
        var configDir = Path.Combine(_temp.Path, "zb");
        var root = CreateGame($"-agentlib:zbNative=config_dir={configDir},policy=allow-all");
        var zabric = SteamZabric();
        _platform.HomeDirectory = "";

        Assert.True(await RunAsync(root));

        Assert.Equal(ZabricBuilder.JarPath(zabric), StoredZabric(Path.Combine(configDir, "config.json")).Jar);
    }

    [Fact]
    public async Task RunAsync_NoConfigFolder_WritesNothing()
    {
        var root = CreateGame();
        SteamZabric();
        _platform.HomeDirectory = "";

        Assert.False(await RunAsync(root));
    }

    [Fact]
    public async Task RunAsync_FoundCopyNotPreloadable_ReturnsTheReasonWithoutWriting()
    {
        var root = CreateGame();
        var zabric = SteamZabric();
        ZabricBuilder.WriteJar(ZabricBuilder.JarPath(zabric), "Manifest-Version: 1.0\r\n\r\n");
        var launch = await LaunchAsync(root);

        var error = ResultAssert.Failed(await _step.RunAsync(launch, Ct), ErrorCode.ZombieBuddyModNotPreloadable);

        Assert.Equal(ZombieBuddyJavaMod.NoManifestPreload, error.Details["reason"]);
        Assert.False(File.Exists(DefaultConfig));
        Assert.False(launch.IsPreloaded("Zabric"));
    }

    [Fact]
    public async Task RunAsync_ConfigInvalid_ReturnsItsErrorWithoutMarkingPreloaded()
    {
        var root = CreateGame();
        SteamZabric();
        Directory.CreateDirectory(Path.GetDirectoryName(DefaultConfig)!);
        await File.WriteAllTextAsync(DefaultConfig, "[]", Ct);
        var launch = await LaunchAsync(root);

        ResultAssert.Failed(await _step.RunAsync(launch, Ct), ErrorCode.ZombieBuddyPreloadConfigInvalid);

        Assert.Equal("[]", await File.ReadAllTextAsync(DefaultConfig, Ct));
        Assert.False(launch.IsPreloaded("Zabric"));
    }

    [Fact]
    public async Task RunAsync_CancelledToken_Throws()
    {
        var launch = await LaunchAsync(CreateGame());
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _step.RunAsync(launch, cts.Token));
    }
}
