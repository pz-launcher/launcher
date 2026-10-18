using System.ComponentModel;
using Microsoft.Extensions.Logging.Abstractions;
using PzLauncher.Core.Common;
using PzLauncher.Core.Platform;
using PzLauncher.Core.Steam;
using PzLauncher.Core.Tests.TestSupport;

namespace PzLauncher.Core.Tests.Steam;

public sealed class SteamLaunchHookServiceTests : IDisposable
{
    private const uint AccountId = 629668875;
    private const string EmptyOptionsLine = "\t\t\t\t\t\t\"LaunchOptions\"\t\t\"\"\n";

    private readonly TempDirectory _temp = new();
    private readonly FakeProcessRunner _processes = new();
    private readonly SteppingTimeProvider _time = new();
    private readonly FakePlatformEnvironment _platform;
    private readonly string _root;

    public SteamLaunchHookServiceTests()
    {
        _root = new SteamLayoutBuilder(_temp.Path).CreateLibrary("Steam");
        _platform = FakePlatformEnvironment.WithRegistrySteam(_root);
        SteamLayoutBuilder.WriteRaw(SteamClientFiles.LoginUsersPath(_root), Fixtures.Read("Steam", "loginusers.vdf"));
        WriteOptions("");
    }

    public void Dispose() => _temp.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string Exe => Path.Combine(_temp.Path, "PzLauncher", "PzLauncher.exe");

    private string OtherExe => Path.Combine(_temp.Path, "Old", "PzLauncher.exe");

    private string SteamExe => Path.Combine(_root, "steam.exe");

    private string ConfigPath => SteamClientFiles.LocalConfigPath(_root, AccountId);

    private SteamLaunchHookService Service() =>
        new(new SteamLocator(_platform), _platform, _processes, _time, NullLogger<SteamLaunchHookService>.Instance);

    private static string Escaped(string options) =>
        options.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);

    private void WriteOptions(string options) =>
        SteamLayoutBuilder.WriteRaw(
            ConfigPath,
            Fixtures.ReadLf("Steam", "localconfig.vdf").Replace(EmptyOptionsLine, $"\t\t\t\t\t\t\"LaunchOptions\"\t\t\"{Escaped(options)}\"\n", StringComparison.Ordinal));

    private async Task<string> ReadOptionsAsync() => ResultAssert.Ok(await LocalConfigEditor.ReadAsync(ConfigPath, PzSteamApps.Game, Ct)).LaunchOptions;

    private static string Hook(string exe) => $"\"{exe}\" %command%";

    // After -shutdown, steam exits once this many listings have still shown it; null: never.
    private FakeRunningProcess SteamRunning(int? listingsBeforeExit = 0)
    {
        var steam = _processes.AddRunning("steam");
        var shutDown = false;
        var listings = 0;
        _processes.OnStart = request => shutDown |= request.Arguments.Contains("-shutdown");
        _processes.OnFindByName = _ =>
        {
            if (shutDown && listings++ == listingsBeforeExit)
            {
                steam.Exit();
            }
        };

        return steam;
    }

    [Fact]
    public async Task GetStatusAsync_NoHook_IsOffForTheCurrentAccount()
    {
        var status = ResultAssert.Ok(await Service().GetStatusAsync(Exe, Ct));

        Assert.Equal(new SteamLaunchHookStatus(new SteamAccount(AccountId, "Example Player"), SteamLaunchHookState.Off, null, false), status);
    }

    [Fact]
    public async Task GetStatusAsync_HookToThisLauncher_IsOn()
    {
        WriteOptions(Hook(Exe.ToUpperInvariant()) + " -debug");
        _platform.PathComparison = StringComparison.OrdinalIgnoreCase;
        SteamRunning();

        var status = ResultAssert.Ok(await Service().GetStatusAsync(Exe, Ct));

        Assert.Equal((SteamLaunchHookState.On, null, true), (status.State, status.HookedPath, status.IsSteamRunning));
    }

    [Fact]
    public async Task GetStatusAsync_HookToAnotherLauncher_IsOtherLauncherWithItsPath()
    {
        WriteOptions(Hook(OtherExe));

        var status = ResultAssert.Ok(await Service().GetStatusAsync(Exe, Ct));

        Assert.Equal((SteamLaunchHookState.OtherLauncher, OtherExe), (status.State, status.HookedPath));
    }

    [Theory]
    [InlineData(PlatformKind.Linux, ErrorCode.SteamPlatformUnsupported)]
    [InlineData(PlatformKind.MacOS, ErrorCode.SteamPlatformUnsupported)]
    public async Task GetStatusAsync_NotWindows_IsPlatformUnsupported(PlatformKind os, ErrorCode code)
    {
        _platform.Os = os;

        ResultAssert.Failed(await Service().GetStatusAsync(Exe, Ct), code);
    }

    [Fact]
    public async Task GetStatusAsync_NoSteam_IsNotInstalled()
    {
        _platform.SteamRegistryPath = Path.Combine(_temp.Path, "NoSteam");

        ResultAssert.Failed(await Service().GetStatusAsync(Exe, Ct), ErrorCode.SteamNotInstalled);
    }

    [Fact]
    public async Task GetStatusAsync_NoLoginUsers_IsNoCurrentAccount()
    {
        File.Delete(SteamClientFiles.LoginUsersPath(_root));

        ResultAssert.Failed(await Service().GetStatusAsync(Exe, Ct), ErrorCode.SteamNoCurrentAccount);
    }

    [Fact]
    public async Task GetStatusAsync_NoLocalConfig_IsAppNotInLocalConfig()
    {
        File.Delete(ConfigPath);

        ResultAssert.Failed(await Service().GetStatusAsync(Exe, Ct), ErrorCode.SteamAppNotInLocalConfig);
    }

    [Fact]
    public async Task SetAsync_EnableWithSteamClosed_HooksBeforeTheUsersOptions()
    {
        WriteOptions("-agentlib:zbNative --");

        var applied = ResultAssert.Ok(await Service().SetAsync(Exe, enabled: true, restartSteam: false, Ct));

        Assert.Equal(Hook(Exe) + " -agentlib:zbNative --", await ReadOptionsAsync());
        Assert.Equal(SteamLaunchHookState.On, applied.Status.State);
        Assert.Empty(applied.Warnings);
        Assert.Empty(_processes.Requests);
        Assert.True(File.Exists(ConfigPath + ".pzlauncher.bak"));
    }

    [Fact]
    public async Task SetAsync_Disable_RestoresTheUsersOptions()
    {
        WriteOptions(Hook(Exe) + " -agentlib:zbNative --");

        var applied = ResultAssert.Ok(await Service().SetAsync(Exe, enabled: false, restartSteam: false, Ct));

        Assert.Equal("-agentlib:zbNative --", await ReadOptionsAsync());
        Assert.Equal(SteamLaunchHookState.Off, applied.Status.State);
    }

    [Fact]
    public async Task SetAsync_EnableOverAnotherLauncher_RepointsTheHook()
    {
        WriteOptions(Hook(OtherExe) + " -debug");

        ResultAssert.Ok(await Service().SetAsync(Exe, enabled: true, restartSteam: false, Ct));

        Assert.Equal(Hook(Exe) + " -debug", await ReadOptionsAsync());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SetAsync_AlreadyInThatState_TouchesNeitherFileNorSteam(bool enabled)
    {
        WriteOptions(enabled ? Hook(Exe) : "-debug");
        SteamRunning();

        var applied = ResultAssert.Ok(await Service().SetAsync(Exe, enabled, restartSteam: true, Ct));

        Assert.Empty(_processes.Requests);
        Assert.False(File.Exists(ConfigPath + ".pzlauncher.bak"));
        Assert.True(applied.Status.IsSteamRunning);
    }

    [Fact]
    public async Task SetAsync_SteamRunningWithoutRestart_IsClientRunningAndWritesNothing()
    {
        SteamRunning();

        ResultAssert.Failed(await Service().SetAsync(Exe, enabled: true, restartSteam: false, Ct), ErrorCode.SteamClientRunning);

        Assert.Equal("", await ReadOptionsAsync());
        Assert.Empty(_processes.Requests);
    }

    [Fact]
    public async Task SetAsync_SteamRunningWithRestart_WaitsForSteamToExitWritesThenStartsSteam()
    {
        var steam = SteamRunning(listingsBeforeExit: 3);
        string? optionsAtRestart = null;
        var exitedAtRestart = false;
        var shutdown = _processes.OnStart!;
        _processes.OnStart = request =>
        {
            shutdown(request);
            if (request.Arguments.Count == 0)
            {
                optionsAtRestart = File.ReadAllText(ConfigPath);
                exitedAtRestart = steam.HasExited;
            }
        };

        var applied = ResultAssert.Ok(await Service().SetAsync(Exe, enabled: true, restartSteam: true, Ct));

        Assert.Equal(2, _processes.Requests.Count);
        Assert.All(_processes.Requests, request => Assert.Equal((SteamExe, _root), (request.FileName, request.WorkingDirectory)));
        Assert.Equal(["-shutdown"], _processes.Requests[0].Arguments);
        Assert.Empty(_processes.Requests[1].Arguments);
        Assert.Contains(Escaped(Hook(Exe)), optionsAtRestart, StringComparison.Ordinal);
        Assert.True(exitedAtRestart);
        Assert.True(applied.Status.IsSteamRunning);
        Assert.Empty(applied.Warnings);
        Assert.All(_processes.Processes, process => Assert.True(process.IsDisposed));
        Assert.True(steam.IsDisposed);
    }

    [Theory]
    [InlineData(false, ErrorCode.SteamShutdownTimedOut)]
    [InlineData(true, ErrorCode.SteamShutdownUnconfirmed)]
    public async Task SetAsync_SteamExitUnconfirmed_FailsWritesNothingAndStartsSteamAgain(bool listingFails, ErrorCode code)
    {
        SteamRunning(listingsBeforeExit: null);
        if (listingFails)
        {
            _processes.OnFindByName = _ =>
            {
                if (_processes.Requests.Count > 0)
                {
                    throw new Win32Exception(5, "Access is denied.");
                }
            };
        }

        ResultAssert.Failed(await Service().SetAsync(Exe, enabled: true, restartSteam: true, Ct), code);

        Assert.Equal(listingFails ? TimeSpan.Zero : TimeSpan.FromSeconds(60), _time.GetElapsedTime(0));
        Assert.Equal("", await ReadOptionsAsync());
        Assert.Equal(2, _processes.Requests.Count);
        Assert.Empty(_processes.Requests[^1].Arguments);
    }

    [Fact]
    public async Task SetAsync_ShutdownCommandFails_IsStartFailedAndWritesNothing()
    {
        SteamRunning();
        _processes.StartException = new Win32Exception(2, "The system cannot find the file specified.");

        var error = ResultAssert.Failed(await Service().SetAsync(Exe, enabled: true, restartSteam: true, Ct), ErrorCode.SteamStartFailed);

        Assert.Equal(SteamExe, error.Details["path"]);
        Assert.Equal("", await ReadOptionsAsync());
    }

    [Fact]
    public async Task SetAsync_RestartFails_SucceedsWithAWarning()
    {
        SteamRunning();
        _processes.StartExceptionFor = request => request.Arguments.Count == 0 ? new Win32Exception(5, "Access is denied.") : null;

        var applied = ResultAssert.Ok(await Service().SetAsync(Exe, enabled: true, restartSteam: true, Ct));

        Assert.Equal(ErrorCode.SteamStartFailed, Assert.Single(applied.Warnings).Code);
        Assert.False(applied.Status.IsSteamRunning);
        Assert.Equal(Hook(Exe), await ReadOptionsAsync());
    }

    [Fact]
    public async Task SetAsync_WriteFails_StillStartsSteamAgain()
    {
        SteamRunning();
        Directory.CreateDirectory(ConfigPath + ".pzlauncher.bak");

        ResultAssert.Failed(await Service().SetAsync(Exe, enabled: true, restartSteam: true, Ct), ErrorCode.SteamLocalConfigUnwritable);

        Assert.Empty(_processes.Requests[^1].Arguments);
    }

    [Fact]
    public async Task SetAsync_FileCantBeEdited_NeverShutsSteamDown()
    {
        SteamRunning();
        await File.WriteAllTextAsync(ConfigPath, "\"UserLocalConfigStore\"\n{\n", Ct);

        ResultAssert.Failed(await Service().SetAsync(Exe, enabled: true, restartSteam: true, Ct), ErrorCode.SteamLocalConfigMalformed);

        Assert.Empty(_processes.Requests);
    }

    [Fact]
    public async Task SetAsync_CancelledWhileSteamShutsDown_ThrowsAndWritesNothing()
    {
        SteamRunning(listingsBeforeExit: null);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        _processes.OnStart = _ => cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service().SetAsync(Exe, enabled: true, restartSteam: true, cts.Token));

        Assert.Equal("", await ReadOptionsAsync());
        Assert.Empty(_processes.Requests[^1].Arguments);
    }
}
