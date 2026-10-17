using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PzLauncher.Core.Common;
using PzLauncher.Core.Mod.ZombieBuddy;
using PzLauncher.Core.Platform;
using PzLauncher.Core.Steam;
using PzLauncher.Core.Tests.Mod.ZBSkipOnlineChecks;
using PzLauncher.Core.Tests.TestSupport;

namespace PzLauncher.Core.Tests.Mod.ZombieBuddy;

public sealed class ZombieBuddyModsServiceTests : IDisposable
{
    private const string AllowAll = "-agentlib:zbNative=policy=allow-all";

    private readonly TempDirectory _temp = new();
    private readonly string _library;
    private readonly string _home;
    private readonly FakePlatformEnvironment _platform;
    private readonly List<(string Step, ZombieBuddyLaunch Launch)> _runs = [];

    public ZombieBuddyModsServiceTests()
    {
        var steam = new SteamLayoutBuilder(_temp.Path);
        _library = steam.CreateLibrary("Library");
        _home = _temp.CreateDirectory("home");
        _platform = FakePlatformEnvironment.WithRegistrySteam(steam.CreateSteamRoot(_library));
        _platform.HomeDirectory = _home;
    }

    public void Dispose() => _temp.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string DefaultConfig => Path.Combine(_home, ".zombie_buddy", "config.json");

    private GameInstallBuilder Installs => new(Path.Combine(_library, "steamapps", "common"), PlatformKind.Windows);

    private string CreateGame() => Installs.CreateFlat("ProjectZomboid", GameInstallBuilder.WindowsGameJsonWith(AllowAll), withJre: false);

    private ZombieBuddyModsService CreateService(params IZombieBuddyLaunchStep[] steps) => new(
        ZombieBuddyServiceTests.CreateService(_platform), ZombieBuddyServiceTests.CreateWorkshopLibraries(_platform), _platform, steps);

    private RecordingStep Step(string name, Result result) => new(name, result, _runs);

    [Fact]
    public async Task PrepareLaunchAsync_Game_RunsEveryStepInOrderWithOneLaunch()
    {
        var root = CreateGame();
        var service = CreateService(Step("first", Result.Ok()), Step("second", Result.Ok()));

        Assert.Empty(await service.PrepareLaunchAsync(root, "", Ct));

        Assert.Equal(["first", "second"], _runs.Select(run => run.Step));
        Assert.Same(_runs[0].Launch, _runs[1].Launch);
        Assert.Equal(root, _runs[0].Launch.Root);
    }

    [Fact]
    public async Task PrepareLaunchAsync_StepsFail_RunsTheRestAndReturnsEveryErrorInOrder()
    {
        var first = ZombieBuddyErrors.ModNotPreloadable("A", "a.info", ZombieBuddyJavaMod.NoJarFile);
        var second = ZombieBuddyErrors.ModNotPreloadable("B", "b.info", ZombieBuddyJavaMod.MissingJar);
        var service = CreateService(Step("first", first), Step("ok", Result.Ok()), Step("second", second));

        var errors = await service.PrepareLaunchAsync(CreateGame(), "", Ct);

        Assert.Equal([first, second], errors);
        Assert.Equal(3, _runs.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PrepareLaunchAsync_NothingToSetUp_RunsNoStep(bool macOS)
    {
        var root = macOS ? CreateGame() : Installs.CreateServer("Server");
        if (macOS)
        {
            _platform.Os = PlatformKind.MacOS;
        }

        Assert.Empty(await CreateService(Step("only", Result.Ok())).PrepareLaunchAsync(root, "", Ct));

        Assert.Empty(_runs);
    }

    [Fact]
    public async Task PrepareLaunchAsync_CancelledToken_Throws()
    {
        var root = CreateGame();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateService(Step("only", Result.Ok())).PrepareLaunchAsync(root, "", cts.Token));
        Assert.Empty(_runs);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PrepareLaunchAsync_RegisteredSteps_PreloadSkipOnlineChecksOnlyAfterZabric(bool zabricInstalled)
    {
        var root = CreateGame();
        if (zabricInstalled)
        {
            ZabricBuilder.Create(Path.Combine(WorkshopLibraries.ItemFolder(_library, "3820726385"), "mods", "Zabric"));
        }

        ZBSkipOnlineChecksLaunchStepTests.SteamCopy(_library);
        ZBSkipOnlineChecksLaunchStepTests.WriteEnabled(Path.Combine(_home, "Zomboid"), "ZombieBuddy", "Zabric", "ZBSkipOnlineChecks");
        var services = new ServiceCollection();
        services.AddSingleton<IPlatformEnvironment>(_platform);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddPzLauncherCore();
        using var provider = services.BuildServiceProvider();

        Assert.Empty(await provider.GetRequiredService<IZombieBuddyModsService>().PrepareLaunchAsync(root, "", Ct));

        if (zabricInstalled)
        {
            var preload = JsonNode.Parse(await File.ReadAllTextAsync(DefaultConfig, Ct))!["preload_mods"]!.AsObject();
            Assert.Equal(["Zabric", "ZBSkipOnlineChecks"], preload.Select(mod => mod.Key));
        }
        else
        {
            Assert.False(File.Exists(DefaultConfig));
        }

        Assert.True(File.Exists(ZBSkipOnlineChecksLaunchStepTests.GameJar(root)));
        Assert.Equal(AllowAll + ",patches_jar=" + ZBSkipOnlineChecksLaunchStepTests.Entry, ZombieBuddyServiceTests.VmArgsOf(root)[0]);
    }

    private sealed class RecordingStep(string name, Result result, List<(string Step, ZombieBuddyLaunch Launch)> runs) : IZombieBuddyLaunchStep
    {
        public Task<Result> RunAsync(ZombieBuddyLaunch launch, CancellationToken ct)
        {
            runs.Add((name, launch));
            return Task.FromResult(result);
        }
    }
}
