using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using PzLauncher.Core.Common;
using PzLauncher.Core.Platform;
using PzLauncher.Core.Steam;
using PzLauncher.Core.Tests.Installs;
using PzLauncher.Core.Tests.TestSupport;
using PzLauncher.Core.ZombieBuddy;

namespace PzLauncher.Core.Tests.ZombieBuddy;

public sealed class ZombieBuddyServiceZabricPreloadTests : IDisposable
{
    private const string AllowAll = "-agentlib:zbNative=policy=allow-all";

    private readonly TempDirectory _temp = new();
    private readonly string _library;
    private readonly string _home;
    private readonly FakePlatformEnvironment _platform;
    private readonly ZombieBuddyService _service;

    public ZombieBuddyServiceZabricPreloadTests()
    {
        var steam = new SteamLayoutBuilder(_temp.Path);
        _library = steam.CreateLibrary("Library");
        _home = _temp.CreateDirectory("home");
        _platform = FakePlatformEnvironment.WithRegistrySteam(steam.CreateSteamRoot(_library));
        _platform.HomeDirectory = _home;
        _service = new ZombieBuddyService(
            InstallDetectionServiceScanTests.CreateService(_platform),
            new SteamLocator(_platform),
            new SteamLibraryReader(),
            _platform,
            NullLogger<ZombieBuddyService>.Instance);
    }

    public void Dispose() => _temp.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string DefaultConfig => Path.Combine(_home, ".zombie_buddy", "config.json");

    private GameInstallBuilder Installs => new(Path.Combine(_library, "steamapps", "common"), PlatformKind.Windows);

    private string CreateGame(string agentArg = AllowAll) =>
        Installs.CreateFlat("ProjectZomboid", GameInstallBuilder.WindowsGameJsonWith(agentArg), withJre: false);

    private string SteamZabric(string? modInfo = null) =>
        ZabricBuilder.Create(Path.Combine(ZombieBuddyFiles.WorkshopContentFolder(_library), "3820726385", "mods", "Zabric"), modInfo: modInfo);

    private static string StagedZabric(string cache, string classes = "v1") =>
        ZabricBuilder.Create(Path.Combine(cache, "Workshop", "Zabric", "Contents", "mods", "Zabric"), classes: classes);

    private async Task<ZabricPreload> EnsureAsync(string root, string extraGameArgs = "") =>
        ResultAssert.Ok(await _service.EnsureZabricPreloadAsync(root, extraGameArgs, Ct));

    private static (string? Inf, string? Jar) StoredZabric(string config)
    {
        var entry = JsonNode.Parse(File.ReadAllText(config))?["preload_mods"]?["Zabric"];
        return (entry?["infPath"]?.GetValue<string>(), entry?["jarPath"]?.GetValue<string>());
    }

    [Fact]
    public async Task EnsureZabricPreloadAsync_AllowAllWithSteamCopy_WritesTheEntryThenIsCurrent()
    {
        var root = CreateGame();
        var zabric = SteamZabric();

        Assert.Equal(ZabricPreload.Written, await EnsureAsync(root));
        Assert.Equal((ZabricBuilder.InfPath(zabric), ZabricBuilder.JarPath(zabric)), StoredZabric(DefaultConfig));

        Assert.Equal(ZabricPreload.Current, await EnsureAsync(root));
    }

    [Theory]
    [InlineData("-agentlib:zbNative")]
    [InlineData("-agentlib:zbNative=policy=prompt")]
    [InlineData("-javaagent:ZombieBuddy.jar=policy=allow-all")]
    public async Task EnsureZabricPreloadAsync_NotAllowAll_IsNotApplicable(string agentArg)
    {
        var root = CreateGame(agentArg);
        SteamZabric();

        Assert.Equal(ZabricPreload.NotApplicable, await EnsureAsync(root));
        Assert.False(File.Exists(DefaultConfig));
    }

    [Fact]
    public async Task EnsureZabricPreloadAsync_NoZabric_IsNotApplicable()
    {
        var root = CreateGame();
        SteamZabric(ZabricBuilder.ModInfo(id: "Other"));

        Assert.Equal(ZabricPreload.NotApplicable, await EnsureAsync(root));
        Assert.False(File.Exists(DefaultConfig));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EnsureZabricPreloadAsync_ServerOrMacOS_IsNotApplicable(bool macOS)
    {
        var root = macOS ? CreateGame() : Installs.CreateServer("Server");
        SteamZabric();
        if (macOS)
        {
            _platform.Os = PlatformKind.MacOS;
        }

        Assert.Equal(ZabricPreload.NotApplicable, await EnsureAsync(root));
        Assert.False(File.Exists(DefaultConfig));
    }

    [Fact]
    public async Task EnsureZabricPreloadAsync_GameJsonMissing_IsNotApplicable()
    {
        var root = Installs.CreateFlat("ProjectZomboid", json: null, withJre: false);
        SteamZabric();

        Assert.Equal(ZabricPreload.NotApplicable, await EnsureAsync(root));
    }

    [Fact]
    public async Task EnsureZabricPreloadAsync_StagedCopies_UsesTheOneInTheCacheDirOfTheGameArgs()
    {
        var root = CreateGame();
        SteamZabric();
        var inHome = StagedZabric(Path.Combine(_home, "Zomboid"));
        var cache = Path.Combine(_temp.Path, "pz cache");
        var inCache = StagedZabric(cache, classes: "v2");

        Assert.Equal(ZabricPreload.Written, await EnsureAsync(root, "-debug"));
        Assert.Equal(ZabricBuilder.JarPath(inHome), StoredZabric(DefaultConfig).Jar);

        Assert.Equal(ZabricPreload.Written, await EnsureAsync(root, $"-cachedir=\"{cache}\" -debug"));
        Assert.Equal(ZabricBuilder.JarPath(inCache), StoredZabric(DefaultConfig).Jar);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EnsureZabricPreloadAsync_ConfigDirOption_WritesThere(bool relative)
    {
        var configDir = relative ? "zb config" : Path.Combine(_temp.Path, "zb");
        var root = CreateGame($"-agentlib:zbNative=config_dir= {configDir} ,policy=allow-all");
        SteamZabric();

        Assert.Equal(ZabricPreload.Written, await EnsureAsync(root));

        Assert.True(File.Exists(Path.Combine(relative ? Path.Combine(root, configDir) : configDir, "config.json")));
        Assert.False(File.Exists(DefaultConfig));
    }

    [Fact]
    public async Task EnsureZabricPreloadAsync_FoundCopyNotPreloadable_ReturnsTheReasonWithoutWriting()
    {
        var root = CreateGame();
        var zabric = SteamZabric();
        ZabricBuilder.WriteJar(ZabricBuilder.JarPath(zabric), "Manifest-Version: 1.0\r\n\r\n");

        var error = ResultAssert.Failed(await _service.EnsureZabricPreloadAsync(root, "", Ct), ErrorCode.ZombieBuddyZabricNotPreloadable);

        Assert.Equal(ZabricModInfo.NoManifestPreload, error.Details["reason"]);
        Assert.False(File.Exists(DefaultConfig));
    }

    [Fact]
    public async Task EnsureZabricPreloadAsync_CancelledToken_Throws()
    {
        var root = CreateGame();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _service.EnsureZabricPreloadAsync(root, "", cts.Token));
    }
}
