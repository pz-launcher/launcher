using System.ComponentModel;
using Microsoft.Extensions.Logging.Abstractions;
using PzLauncher.Core.Common;
using PzLauncher.Core.Java;
using PzLauncher.Core.Launch;
using PzLauncher.Core.Platform;
using PzLauncher.Core.Tests.Installs;
using PzLauncher.Core.Tests.Java;
using PzLauncher.Core.Tests.TestSupport;

namespace PzLauncher.Core.Tests.Launch;

public sealed class GameLaunchServiceTests : IDisposable
{
    private const string MainClass = "zombie.gameStates.MainScreenState";

    private static readonly StringComparison HostPathComparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private readonly TempDirectory _temp = new();
    private readonly FakeProcessRunner _runner = new();
    private FakePlatformEnvironment _platform = Platform(PlatformKind.Windows);
    private GameInstallBuilder _installs;
    private GameLaunchService _service;

    public GameLaunchServiceTests()
    {
        _installs = new GameInstallBuilder(_temp.Path, PlatformKind.Windows);
        _service = CreateService();
    }

    public void Dispose() => _temp.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static FakePlatformEnvironment Platform(PlatformKind os) => new()
    {
        Os = os,
        PathComparison = HostPathComparison,
        OsVersion = os == PlatformKind.Windows ? new Version(10, 0, 26100, 0) : new Version(6, 8),
        TotalMemoryBytes = 32L * 1024 * 1024 * 1024,
    };

    private GameLaunchService CreateService() => new(
        InstallDetectionServiceScanTests.CreateService(_platform),
        JavaDiscoveryServiceScanTests.CreateService(_platform),
        _platform,
        _runner,
        NullLogger<GameLaunchService>.Instance);

    private void UseLinux()
    {
        _platform = Platform(PlatformKind.Linux);
        _installs = new GameInstallBuilder(_temp.Path, PlatformKind.Linux);
        _service = CreateService();
    }

    private Task<Result<GameSession>> LaunchAsync(string root, GameLaunchOverrides? overrides = null) =>
        _service.LaunchAsync(new LaunchGameRequest(root, overrides ?? GameLaunchOverrides.Empty), Ct);

    private static GameLaunchOverrides WithMemory(int? max, int? min) => GameLaunchOverrides.Empty with { MaxMemoryMb = max, MinMemoryMb = min };

    [Fact]
    public async Task LaunchAsync_WindowsInstall_StartsBundledJavawWithJsonArgsInGameFolder()
    {
        var root = _installs.CreateFlat("PZ", GameInstallBuilder.WindowsGameJson);

        var session = ResultAssert.Ok(await LaunchAsync(root));

        var request = _runner.LastRequest;
        Assert.Equal(Path.Combine(root, "jre64", "bin", "javaw.exe"), request.FileName);
        Assert.Equal(root, request.WorkingDirectory);
        Assert.Empty(request.Environment);
        Assert.Equal(
            [
                "-Djava.awt.headless=true", "--enable-native-access=ALL-UNNAMED", "--add-exports=java.base/jdk.internal.misc=ALL-UNNAMED",
                "-Xmx3072m", "-Dzomboid.steam=1", "-Dzomboid.znetlog=1", "-Djava.library.path=win64/;.", "-XX:-CreateCoredumpOnCrash",
                "-XX:-OmitStackTraceInFastThrow", "-XX:+UseZGC", "-cp", ".;projectzomboid.jar", MainClass,
            ],
            request.Arguments);
        Assert.Equal(new GameSession(root, _runner.Processes[0].Id), session);
    }

    [Fact]
    public async Task LaunchAsync_Overrides_ReachTheCommand()
    {
        var root = _installs.CreateFlat("PZ", GameInstallBuilder.WindowsGameJson);

        ResultAssert.Ok(await LaunchAsync(root, new GameLaunchOverrides(null, 8192, 4096, "-Da=1", "-debug")));

        var args = _runner.LastRequest.Arguments;
        Assert.DoesNotContain("-Xmx3072m", args);
        Assert.Equal(["-Xmx8192m", "-Xms4096m", "-Da=1", "-cp", ".;projectzomboid.jar", MainClass, "-debug"], args.TakeLast(7));
    }

    [Fact]
    public async Task LaunchAsync_ChosenJavaHome_StartsItsExecutable()
    {
        var root = _installs.CreateFlat("PZ", GameInstallBuilder.WindowsGameJson);
        var javaHome = new JavaHomeBuilder(_temp.Path, PlatformKind.Windows).Create("jdk-25");

        ResultAssert.Ok(await LaunchAsync(root, GameLaunchOverrides.Empty with { JavaHome = javaHome + Path.DirectorySeparatorChar }));

        Assert.Equal(Path.Combine(javaHome, "bin", "javaw.exe"), _runner.LastRequest.FileName);
    }

    [Fact]
    public async Task LaunchAsync_LinuxSteamLayout_RunsInNestedFolderWithScriptEnvironment()
    {
        UseLinux();
        var root = _installs.CreateNested("ProjectZomboid", GameInstallBuilder.LinuxGameJson);
        var game = GameInstallBuilder.GameFolder(root, nested: true);

        var session = ResultAssert.Ok(await LaunchAsync(root));

        var request = _runner.LastRequest;
        var jre = Path.Combine(game, "jre64");
        Assert.Equal(Path.Combine(jre, "bin", "java"), request.FileName);
        Assert.Equal(game, request.WorkingDirectory);
        Assert.Equal(["-cp", ".:projectzomboid.jar", MainClass], request.Arguments.TakeLast(3));
        Assert.DoesNotContain("-XX:+UseG1GC", request.Arguments);
        Assert.Equal(3, request.Environment.Count);
        Assert.Equal($"{Path.Combine(game, "natives")}:{game}:{Path.Combine(jre, "lib")}", request.Environment["LD_LIBRARY_PATH"]);
        Assert.Equal("libjsig.so:libPZXInitThreads64.so", request.Environment["LD_PRELOAD"]);
        Assert.Equal("", request.Environment["XMODIFIERS"]);
        Assert.Equal(root, session.InstallRoot);
    }

    [Fact]
    public async Task LaunchAsync_LinuxInheritedLoaderVariables_AreKept()
    {
        UseLinux();
        _platform.EnvironmentVariables["LD_LIBRARY_PATH"] = "/opt/lib";
        _platform.EnvironmentVariables["LD_PRELOAD"] = "/usr/lib/libgamemodeauto.so";
        var root = _installs.CreateFlat("pz", GameInstallBuilder.LinuxGameJson);
        var javaHome = new JavaHomeBuilder(_temp.Path, PlatformKind.Linux).Create("jdk-25");

        ResultAssert.Ok(await LaunchAsync(root, GameLaunchOverrides.Empty with { JavaHome = javaHome }));

        var environment = _runner.LastRequest.Environment;
        Assert.Equal(Path.Combine(javaHome, "bin", "java"), _runner.LastRequest.FileName);
        Assert.Equal($"{Path.Combine(root, "natives")}:{root}:{Path.Combine(javaHome, "lib")}:/opt/lib", environment["LD_LIBRARY_PATH"]);
        Assert.Equal("/usr/lib/libgamemodeauto.so:libjsig.so:libPZXInitThreads64.so", environment["LD_PRELOAD"]);
    }

    [Fact]
    public async Task LaunchAsync_MacOS_ReturnsPlatformUnsupported()
    {
        _platform.Os = PlatformKind.MacOS;
        var root = _installs.CreateFlat("PZ", GameInstallBuilder.WindowsGameJson);

        ResultAssert.Failed(await LaunchAsync(root), ErrorCode.LaunchPlatformUnsupported);
        Assert.Empty(_runner.Requests);
    }

    [Theory]
    [InlineData(0, null, 0)]
    [InlineData(-1, null, -1)]
    [InlineData(null, 0, 0)]
    [InlineData(4096, 8192, 8192)]
    [InlineData(null, 4096, 4096)]
    public async Task LaunchAsync_InvalidMemory_ReturnsInvalidMemory(int? max, int? min, int rejected)
    {
        // The fixture's -Xmx3072m is the effective maximum when max isn't overridden.
        var root = _installs.CreateFlat("PZ", GameInstallBuilder.WindowsGameJson);

        var error = ResultAssert.Failed(await LaunchAsync(root, WithMemory(max, min)), ErrorCode.LaunchInvalidMemory);

        Assert.Equal(rejected.ToString(System.Globalization.CultureInfo.InvariantCulture), error.Details["value"]);
        Assert.Empty(_runner.Requests);
    }

    [Fact]
    public async Task LaunchAsync_MinOverrideWithoutAnyMax_IsAllowed()
    {
        var root = _installs.CreateFlat("PZ", """{"mainClass":"a/B","classpath":["."]}""");

        ResultAssert.Ok(await LaunchAsync(root, WithMemory(null, 4096)));
    }

    [Fact]
    public async Task LaunchAsync_GameRunning_ReturnsAlreadyRunning()
    {
        var root = _installs.CreateFlat("PZ", GameInstallBuilder.WindowsGameJson);
        ResultAssert.Ok(await LaunchAsync(root));

        ResultAssert.Failed(await LaunchAsync(root), ErrorCode.LaunchAlreadyRunning);
        Assert.Single(_runner.Requests);
    }

    [Fact]
    public async Task LaunchAsync_AfterTrackedGameExits_StartsAgainAndDisposesTheOldProcess()
    {
        var root = _installs.CreateFlat("PZ", GameInstallBuilder.WindowsGameJson);
        ResultAssert.Ok(await LaunchAsync(root));

        _runner.Processes[0].Exit();
        await _service.WaitForExitAsync(Ct);

        Assert.True(_runner.Processes[0].IsDisposed);
        ResultAssert.Ok(await LaunchAsync(root));
        Assert.Equal(2, _runner.Requests.Count);
    }

    [Fact]
    public async Task LaunchAsync_PathNotFound_PassesInstallErrorThrough() =>
        ResultAssert.Failed(await LaunchAsync(Path.Combine(_temp.Path, "missing")), ErrorCode.InstallPathNotFound);

    [Fact]
    public async Task LaunchAsync_ServerFolder_ReturnsNotAGame()
    {
        var root = _installs.CreateServer("Server");

        var error = ResultAssert.Failed(await LaunchAsync(root), ErrorCode.LaunchNotAGame);

        Assert.Equal(root, error.Details["path"]);
    }

    [Fact]
    public async Task LaunchAsync_NoConfig_ReturnsConfigMissingForTheRoot()
    {
        var root = _installs.CreateFlat("PZ", json: null);

        var error = ResultAssert.Failed(await LaunchAsync(root), ErrorCode.LaunchConfigMissing);

        Assert.Equal(Path.Combine(root, "ProjectZomboid64.json"), error.Details["path"]);
    }

    [Fact]
    public async Task LaunchAsync_MalformedConfig_ReturnsConfigMalformed()
    {
        var root = _installs.CreateFlat("PZ", """{"mainClass":""");

        ResultAssert.Failed(await LaunchAsync(root), ErrorCode.LaunchConfigMalformed);
    }

    [Fact]
    public async Task LaunchAsync_BundledJavaMissing_ReturnsJavaMissing()
    {
        var root = _installs.CreateFlat("PZ", GameInstallBuilder.WindowsGameJson, withJre: false);

        var error = ResultAssert.Failed(await LaunchAsync(root), ErrorCode.LaunchJavaMissing);

        Assert.Equal(Path.Combine(root, "jre64", "bin", "javaw.exe"), error.Details["path"]);
    }

    [Theory]
    [InlineData("gone")]
    [InlineData("relative/jdk")]
    [InlineData("")]
    public async Task LaunchAsync_ChosenJavaMissing_ReturnsJavaMissing(string javaHome)
    {
        var root = _installs.CreateFlat("PZ", GameInstallBuilder.WindowsGameJson);
        var home = javaHome == "gone" ? Path.Combine(_temp.Path, "gone") : javaHome;

        ResultAssert.Failed(await LaunchAsync(root, GameLaunchOverrides.Empty with { JavaHome = home }), ErrorCode.LaunchJavaMissing);
        Assert.Empty(_runner.Requests);
    }

    public static TheoryData<Exception> StartExceptions =>
    [
        new Win32Exception(5, "Access is denied"),
        new IOException("disk"),
        new UnauthorizedAccessException("denied"),
    ];

    [Theory]
    [MemberData(nameof(StartExceptions))]
    public async Task LaunchAsync_OsRefusesStart_ReturnsStartFailedAndTracksNothing(Exception exception)
    {
        var root = _installs.CreateFlat("PZ", GameInstallBuilder.WindowsGameJson);
        _runner.StartException = exception;

        var error = ResultAssert.Failed(await LaunchAsync(root), ErrorCode.LaunchStartFailed);

        Assert.Equal(Path.Combine(root, "jre64", "bin", "javaw.exe"), error.Details["path"]);
        Assert.Equal(exception.Message, error.Details["reason"]);
        Assert.True(_service.WaitForExitAsync(Ct).IsCompleted);
    }

    [Fact]
    public async Task LaunchAsync_Cancelled_Throws()
    {
        var root = _installs.CreateFlat("PZ", GameInstallBuilder.WindowsGameJson);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _service.LaunchAsync(new LaunchGameRequest(root, GameLaunchOverrides.Empty), cts.Token));
        Assert.Empty(_runner.Requests);
    }

    [Fact]
    public void WaitForExitAsync_NothingRunning_CompletesImmediately() =>
        Assert.True(_service.WaitForExitAsync(Ct).IsCompletedSuccessfully);

    [Fact]
    public async Task WaitForExitAsync_GameRunning_CompletesWhenItExits()
    {
        ResultAssert.Ok(await LaunchAsync(_installs.CreateFlat("PZ", GameInstallBuilder.WindowsGameJson)));
        var wait = _service.WaitForExitAsync(Ct);

        Assert.False(wait.IsCompleted);
        _runner.Processes[0].Exit();
        await wait;
    }

    [Fact]
    public async Task WaitForExitAsync_Cancelled_ThrowsWhileGameKeepsRunning()
    {
        var root = _installs.CreateFlat("PZ", GameInstallBuilder.WindowsGameJson);
        ResultAssert.Ok(await LaunchAsync(root));
        using var cts = new CancellationTokenSource();

        var wait = _service.WaitForExitAsync(cts.Token);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wait);
        ResultAssert.Failed(await LaunchAsync(root), ErrorCode.LaunchAlreadyRunning);
    }

    [Fact]
    public async Task GetDefaultsAsync_WindowsInstall_ReturnsJsonMemoryArgsBundledJavaAndSystemMemory()
    {
        var root = _installs.CreateFlat("PZ", GameInstallBuilder.WindowsGameJson);

        var defaults = ResultAssert.Ok(await _service.GetDefaultsAsync(root, Ct));

        Assert.Equal(3072, defaults.MaxMemoryMb);
        Assert.Null(defaults.MinMemoryMb);
        Assert.Equal("-XX:+UseZGC", defaults.VmArgs[^1]);
        Assert.Equal(10, defaults.VmArgs.Count);
        Assert.NotNull(defaults.BundledJava);
        Assert.Equal(25, defaults.BundledJava.Version?.Major);
        Assert.Equal("Azul Systems, Inc.", defaults.BundledJava.Vendor);
        Assert.Equal(32 * 1024, defaults.SystemMemoryMb);
        Assert.Empty(defaults.Warnings);
    }

    [Fact]
    public async Task GetDefaultsAsync_LinuxSteamLayout_ReadsNestedFolder()
    {
        UseLinux();
        var root = _installs.CreateNested("ProjectZomboid", GameInstallBuilder.LinuxGameJson);

        var defaults = ResultAssert.Ok(await _service.GetDefaultsAsync(root, Ct));

        Assert.Equal(3072, defaults.MaxMemoryMb);
        Assert.Equal(JavaDiscoveryService.ResolveRealPath(Path.Combine(root, "projectzomboid", "jre64")), defaults.BundledJava?.HomePath);
    }

    [Fact]
    public async Task GetDefaultsAsync_ConfigMissingAndNoJre_ReturnsWarningAndSystemMemory()
    {
        var root = _installs.CreateFlat("PZ", json: null, withJre: false);

        var defaults = ResultAssert.Ok(await _service.GetDefaultsAsync(root, Ct));

        Assert.Equal(ErrorCode.LaunchConfigMissing, Assert.Single(defaults.Warnings).Code);
        Assert.Null(defaults.MaxMemoryMb);
        Assert.Null(defaults.MinMemoryMb);
        Assert.Null(defaults.BundledJava);
        Assert.Empty(defaults.VmArgs);
        Assert.Equal(32 * 1024, defaults.SystemMemoryMb);
    }

    [Fact]
    public async Task GetDefaultsAsync_ServerFolder_ReturnsNotAGame() =>
        ResultAssert.Failed(await _service.GetDefaultsAsync(_installs.CreateServer("Server"), Ct), ErrorCode.LaunchNotAGame);
}
