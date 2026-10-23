using System.ComponentModel;
using PzLauncher.Core.Common;
using PzLauncher.Core.Platform.Processes;
using PzLauncher.Core.Platform;
using PzLauncher.Core.Server;
using PzLauncher.Core.Tests.TestSupport;

namespace PzLauncher.Core.Tests.Server;

public sealed class ServerHostServiceRunningTests : IDisposable
{
    private readonly ServerHostTestKit _kit = new();

    public void Dispose() => _kit.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<ServerHostService> StartedAsync(TimeProvider? time = null, TimeSpan? stopTimeout = null)
    {
        _kit.AddServer("pz");
        var service = _kit.Service(time, stopTimeout);
        ResultAssert.Ok(await service.StartAsync(new StartServerRequest(_kit.Site, "pz", null, null), Ct));
        return service;
    }

    private async Task<ServerHostService> RunningAsync(TimeSpan? stopTimeout = null)
    {
        var service = await StartedAsync(stopTimeout: stopTimeout);
        Assert.Equal(ServerState.Running, (await StatusAsync(service)).State);
        return service;
    }

    private async Task<ServerStatus> StatusAsync(ServerHostService service) => ResultAssert.Ok(await service.GetStatusAsync(_kit.Site, Ct));

    [Fact]
    public async Task GetStatusAsync_NothingStarted_IsStopped() =>
        Assert.Equal(new ServerStatus(ServerState.Stopped, null, null, null, null), await StatusAsync(_kit.Service()));

    [Fact]
    public async Task GetStatusAsync_RconAnswers_IsRunningAndRemembersIt()
    {
        var service = await StartedAsync();

        var status = await StatusAsync(service);

        Assert.Equal(new ServerStatus(ServerState.Running, "pz", 4001, _kit.Processes.DetachedStartTime, null), status);
        Assert.True(_kit.Running.Ready);
        Assert.Equal([(27015, "pw")], _kit.Rcon.Logins);
        await StatusAsync(service);
        Assert.Single(_kit.Rcon.Logins);
    }

    [Fact]
    public async Task GetStatusAsync_RconNotAnswering_StaysStartingAndProbesAtMostEvery2Seconds()
    {
        var time = new ManualTimeProvider();
        var service = await StartedAsync(time);
        _kit.Rcon.Accepts = false;

        Assert.Equal(ServerState.Starting, (await StatusAsync(service)).State);
        _kit.Rcon.Accepts = true;
        time.Advance(TimeSpan.FromSeconds(1.9));
        Assert.Equal(ServerState.Starting, (await StatusAsync(service)).State);
        time.Advance(TimeSpan.FromSeconds(0.1));
        Assert.Equal(ServerState.Running, (await StatusAsync(service)).State);

        Assert.Equal(2, _kit.Rcon.Logins.Count);
    }

    [Fact]
    public async Task GetStatusAsync_NewServiceInstance_FindsTheServerAgain()
    {
        await RunningAsync();

        var status = await StatusAsync(_kit.Service());

        Assert.Equal(new ServerStatus(ServerState.Running, "pz", 4001, _kit.Processes.DetachedStartTime, null), status);
    }

    public static TheoryData<string> StaleProcesses => ["gone", "other start time", "not java"];

    [Theory]
    [MemberData(nameof(StaleProcesses))]
    public async Task GetStatusAsync_RecordedProcessIsNotTheServer_IsStoppedAndForgetsIt(string stale)
    {
        await RunningAsync();
        var info = _kit.Processes.Alive[4001];
        switch (stale)
        {
            case "gone":
                _kit.Processes.Alive.Remove(4001);
                break;
            case "other start time":
                _kit.Processes.Alive[4001] = info with { StartTime = info.StartTime.AddSeconds(5) };
                break;
            default:
                _kit.Processes.Alive[4001] = info with { Name = "notepad" };
                break;
        }

        Assert.Equal(ServerState.Stopped, (await StatusAsync(_kit.Service())).State);
        Assert.False(File.Exists(_kit.RunningFile));
    }

    [Fact]
    public async Task GetStatusAsync_StartTimeWithinASecond_IsTheSameProcess()
    {
        await RunningAsync();
        _kit.Processes.Alive[4001] = _kit.Processes.Alive[4001] with { StartTime = _kit.Processes.DetachedStartTime.AddMilliseconds(900) };

        Assert.Equal(ServerState.Running, (await StatusAsync(_kit.Service())).State);
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("null")]
    [InlineData("""{"processId":1}""")]
    public async Task GetStatusAsync_MalformedRunningFile_IsStoppedAndDeletesIt(string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_kit.RunningFile)!);
        await File.WriteAllTextAsync(_kit.RunningFile, content, Ct);

        Assert.Equal(ServerState.Stopped, (await StatusAsync(_kit.Service())).State);
        Assert.False(File.Exists(_kit.RunningFile));
    }

    [Fact]
    public async Task GetStatusAsync_ExitedBeforeReady_ReportsExitedEarlyWithTheLastConsoleLines()
    {
        var service = await StartedAsync();
        _kit.Rcon.Accepts = false;
        _kit.WriteConsole(Enumerable.Range(1, 40).Select(i => $"line {i}"));
        _kit.ExitServer();

        var status = await StatusAsync(service);

        Assert.Equal(ServerState.Stopped, status.State);
        Assert.NotNull(status.LastFailure);
        Assert.Equal(ErrorCode.ServerExitedEarly, status.LastFailure.Code);
        Assert.Equal(string.Join('\n', Enumerable.Range(11, 30).Select(i => $"line {i}")), status.LastFailure.Details["output"]);
        Assert.False(File.Exists(_kit.RunningFile));
    }

    [Fact]
    public async Task GetStatusAsync_ExitedAtTheAdminPrompt_ReportsAdminPasswordRequired()
    {
        var service = await StartedAsync();
        _kit.Rcon.Accepts = false;
        Directory.CreateDirectory(_kit.Site.CacheDir);
        File.Copy(Fixtures.Path("Server", "console-admin-prompt.txt"), _kit.ConsolePath);
        _kit.ExitServer();

        var status = await StatusAsync(service);

        Assert.Equal(ErrorCode.ServerAdminPasswordRequired, status.LastFailure?.Code);
    }

    [Fact]
    public async Task GetStatusAsync_ConsoleOlderThanTheStart_ReportsNoOutput()
    {
        var service = await StartedAsync();
        _kit.Rcon.Accepts = false;
        _kit.WriteConsole(["Enter new administrator password: (an earlier run)"]);
        File.SetLastWriteTimeUtc(_kit.ConsolePath, _kit.Processes.DetachedStartTime.UtcDateTime.AddMinutes(-5));
        _kit.ExitServer();

        var status = await StatusAsync(service);

        Assert.Equal(ErrorCode.ServerExitedEarly, status.LastFailure?.Code);
        Assert.Equal("", status.LastFailure?.Details["output"]);
    }

    [Fact]
    public async Task GetStatusAsync_ExitedAfterReady_ReportsExitedUnexpectedlyUntilTheNextStart()
    {
        var service = await RunningAsync();
        _kit.WriteConsole(["crash"]);
        _kit.ExitServer();

        Assert.Equal(ErrorCode.ServerExitedUnexpectedly, (await StatusAsync(service)).LastFailure?.Code);
        Assert.Equal(ErrorCode.ServerExitedUnexpectedly, (await StatusAsync(service)).LastFailure?.Code);

        ResultAssert.Ok(await service.StartAsync(new StartServerRequest(_kit.Site, "pz", null, null), Ct));
        Assert.Null((await StatusAsync(service)).LastFailure);
    }

    [Fact]
    public async Task StopAsync_Running_SendsQuitAndWaitsForTheExit()
    {
        var service = await RunningAsync();
        var lookupsAfterQuit = 0;
        var quit = false;
        _kit.Rcon.OnCommand = command => quit = command == "quit";
        _kit.Processes.OnGetById = id =>
        {
            if (quit && ++lookupsAfterQuit == 3)
            {
                _kit.Processes.Alive.Remove(id);
            }
        };

        var status = ResultAssert.Ok(await service.StopAsync(_kit.Site, Ct));

        Assert.Equal(new ServerStatus(ServerState.Stopped, null, null, null, null), status);
        Assert.Equal("quit", Assert.Single(_kit.Rcon.Commands).Command);
        Assert.Equal(3, lookupsAfterQuit);
        Assert.False(File.Exists(_kit.RunningFile));
        Assert.Null((await StatusAsync(service)).LastFailure);
    }

    [Fact]
    public async Task StopAsync_NoExit_FailsStopTimeoutAndStaysStopping()
    {
        var service = await RunningAsync(stopTimeout: TimeSpan.FromSeconds(10));

        var error = ResultAssert.Failed(await service.StopAsync(_kit.Site, Ct), ErrorCode.ServerStopTimeout);

        Assert.Equal("10", error.Details["seconds"]);
        Assert.Equal(ServerState.Stopping, (await StatusAsync(service)).State);
        Assert.NotNull(_kit.Running.StopRequestedAt);
    }

    [Fact]
    public async Task StopAsync_AlreadyStopping_WaitsWithoutASecondQuit()
    {
        var service = await RunningAsync(stopTimeout: TimeSpan.FromSeconds(10));
        ResultAssert.Failed(await service.StopAsync(_kit.Site, Ct), ErrorCode.ServerStopTimeout);
        var lookups = 0;
        _kit.Processes.OnGetById = id =>
        {
            if (++lookups == 2)
            {
                _kit.Processes.Alive.Remove(id);
            }
        };

        ResultAssert.Ok(await service.StopAsync(_kit.Site, Ct));

        Assert.Single(_kit.Rcon.Commands);
    }

    [Fact]
    public async Task StopAsync_Starting_FailsNotReady()
    {
        var service = await StartedAsync();
        _kit.Rcon.Accepts = false;

        ResultAssert.Failed(await service.StopAsync(_kit.Site, Ct), ErrorCode.ServerNotReady);
        Assert.Empty(_kit.Rcon.Commands);
    }

    [Fact]
    public async Task StopAsync_NothingRuns_FailsNotRunning() =>
        ResultAssert.Failed(await _kit.Service().StopAsync(_kit.Site, Ct), ErrorCode.ServerNotRunning);

    [Fact]
    public async Task StopAsync_QuitFails_FailsRconFailed()
    {
        var service = await RunningAsync();
        _kit.Rcon.Respond = _ => ServerErrors.RconFailed("reset");

        ResultAssert.Failed(await service.StopAsync(_kit.Site, Ct), ErrorCode.ServerRconFailed);
        Assert.Null(_kit.Running.StopRequestedAt);
    }

    [Fact]
    public async Task StopAsync_Cancelled_Throws()
    {
        var service = await RunningAsync();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        _kit.Rcon.OnCommand = _ => cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.StopAsync(_kit.Site, cts.Token));
    }

    [Fact]
    public async Task KillAsync_Starting_KillsTheTreeAndForgetsIt()
    {
        var service = await StartedAsync();
        _kit.Rcon.Accepts = false;

        var status = ResultAssert.Ok(await service.KillAsync(_kit.Site, Ct));

        Assert.Equal(ServerState.Stopped, status.State);
        Assert.Equal([4001], _kit.Processes.Killed);
        Assert.False(File.Exists(_kit.RunningFile));
        Assert.Null((await StatusAsync(service)).LastFailure);
    }

    [Fact]
    public async Task KillAsync_ProcessIdReused_KillsNothing()
    {
        var service = await RunningAsync();
        _kit.Processes.Alive[4001] = new ProcessInfo(4001, "notepad", _kit.Processes.DetachedStartTime);

        ResultAssert.Failed(await service.KillAsync(_kit.Site, Ct), ErrorCode.ServerNotRunning);
        Assert.Empty(_kit.Processes.Killed);
    }

    [Fact]
    public async Task KillAsync_Refused_FailsStopTimeout()
    {
        var service = await RunningAsync();
        _kit.Processes.KillException = new Win32Exception(5, "Access is denied.");

        ResultAssert.Failed(await service.KillAsync(_kit.Site, Ct), ErrorCode.ServerStopTimeout);
        Assert.Equal(ServerState.Stopping, (await StatusAsync(service)).State);
    }

    [Fact]
    public async Task SendCommandAsync_Running_ReturnsTheResponseUsingTheRconSettingsOfTheStart()
    {
        var service = await RunningAsync();
        _kit.WriteIni("pz", "RCONPort=1\nRCONPassword=edited\n");
        _kit.Rcon.Respond = _ => "Players connected (0): ";

        var response = ResultAssert.Ok(await service.SendCommandAsync(_kit.Site, "players", Ct));

        Assert.Equal("Players connected (0): ", response);
        Assert.Equal((27015, "pw", "players"), Assert.Single(_kit.Rcon.Commands));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("say a\nquit")]
    [InlineData("say a\r")]
    public async Task SendCommandAsync_NotOneLine_FailsCommandInvalid(string command)
    {
        var service = await RunningAsync();

        ResultAssert.Failed(await service.SendCommandAsync(_kit.Site, command, Ct), ErrorCode.ServerCommandInvalid);
        Assert.Empty(_kit.Rcon.Commands);
    }

    [Theory]
    [InlineData(2000, null)]
    [InlineData(2001, ErrorCode.ServerCommandInvalid)]
    public async Task SendCommandAsync_Length_IsLimitedTo4000Utf8Bytes(int twoByteChars, ErrorCode? expected)
    {
        var service = await RunningAsync();

        var result = await service.SendCommandAsync(_kit.Site, new string('é', twoByteChars), Ct);

        Assert.Equal(expected, result.Error?.Code);
    }

    [Fact]
    public async Task SendCommandAsync_Starting_FailsNotReady()
    {
        var service = await StartedAsync();
        _kit.Rcon.Accepts = false;

        ResultAssert.Failed(await service.SendCommandAsync(_kit.Site, "players", Ct), ErrorCode.ServerNotReady);
    }

    [Fact]
    public async Task SendCommandAsync_NothingRuns_FailsNotRunning() =>
        ResultAssert.Failed(await _kit.Service().SendCommandAsync(_kit.Site, "players", Ct), ErrorCode.ServerNotRunning);

    [Fact]
    public async Task SendCommandAsync_RconFails_FailsRconFailed()
    {
        var service = await RunningAsync();
        _kit.Rcon.Respond = _ => ServerErrors.RconFailed("reset");

        ResultAssert.Failed(await service.SendCommandAsync(_kit.Site, "players", Ct), ErrorCode.ServerRconFailed);
    }

    [Fact]
    public async Task StreamConsoleAsync_ReadsTheSitesConsoleFile()
    {
        _kit.WriteConsole(["one", "two"]);

        var lines = new List<string>();
        await foreach (var line in _kit.Service().StreamConsoleAsync(_kit.Site, 10, Ct))
        {
            lines.Add(line.Text);
            if (lines.Count == 2)
            {
                break;
            }
        }

        Assert.Equal(["one", "two"], lines);
    }

    [Fact]
    public void StreamConsoleAsync_RelativeSiteFolder_Throws() =>
        Assert.Throws<ArgumentException>(() => _kit.Service().StreamConsoleAsync(_kit.Site with { CacheDir = "cache" }, 10, Ct));
}
