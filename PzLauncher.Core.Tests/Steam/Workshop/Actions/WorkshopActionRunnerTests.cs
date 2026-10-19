using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using PzLauncher.Core.Common;
using PzLauncher.Core.Launch;
using PzLauncher.Core.Platform;
using PzLauncher.Core.Steam.Workshop;
using PzLauncher.Core.Steam.Workshop.Actions;
using PzLauncher.Core.Steam.Workshop.Helper;
using PzLauncher.Core.Tests.TestSupport;
using PzLauncher.Core.Update;

namespace PzLauncher.Core.Tests.Steam.Workshop.Actions;

public sealed class WorkshopActionRunnerTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly FakeProcessRunner _processes = new();
    private readonly FakePlatformEnvironment _platform = new();
    private readonly string _root;

    public WorkshopActionRunnerTests()
    {
        _root = new GameInstallBuilder(_temp.Path, PlatformKind.Windows).CreateFlat("ProjectZomboid", GameInstallBuilder.WindowsGameJson, withJre: false);
        File.WriteAllText(SteamApi, "");
    }

    public void Dispose() => _temp.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string SteamApi => Path.Combine(_root, WorkshopFiles.WindowsSteamApi);

    private string Executable => Path.Combine(_temp.Path, "PzLauncher", "PzLauncher.exe");

    private WorkshopActionRunner Runner(TimeProvider? time = null) =>
        new(_platform, _processes, time ?? new ManualTimeProvider(), NullLogger<WorkshopActionRunner>.Instance) { HelperExecutable = Executable };

    private static WorkshopActionRequest Update(params ulong[] ids) => new(WorkshopAction.Update, ids);

    private static string Line(SteamHelperEvent e) => JsonSerializer.Serialize(e, SteamHelperJsonContext.Default.SteamHelperEvent);

    private static SteamHelperRequest SentRequest(FakePipedProcess process) =>
        JsonSerializer.Deserialize(process.Input.TrimEnd('\n'), SteamHelperJsonContext.Default.SteamHelperRequest)
        ?? throw new InvalidOperationException("no request");

    private void Script(params SteamHelperEvent[] events) =>
        _processes.OnStartPiped = process =>
        {
            process.Output([.. events.Select(Line)]);
            process.Finish(events.OfType<SteamHelperFailed>().Any() ? 1 : 0);
        };

    [Fact]
    public async Task RunAsync_StartsTheLauncherAsHelper_WithAppIdAndRequest()
    {
        Script(new SteamHelperStarted(), new SteamHelperItemDone(1), new SteamHelperItemDone(2), new SteamHelperFinished());

        ResultAssert.Ok(await Runner().RunAsync(_root, Update(1, 2), null, Ct));

        var start = Assert.Single(_processes.Requests);
        Assert.Equal(Executable, start.FileName);
        Assert.Equal([LauncherArguments.SteamHelper], start.Arguments);
        Assert.Equal(_root, start.WorkingDirectory);
        Assert.Equal("108600", start.Environment["SteamAppId"]);
        Assert.Equal("108600", start.Environment["SteamGameId"]);
        var process = Assert.Single(_processes.PipedProcesses);
        Assert.True(process.InputClosed);
        Assert.EndsWith("\n", process.Input, StringComparison.Ordinal);
        var sent = SentRequest(process);
        Assert.Equal((SteamHelperAction.Update, SteamApi), (sent.Action, sent.SteamApiPath));
        Assert.Equal([1UL, 2UL], sent.Ids);
        Assert.True(process.IsDisposed);
        Assert.False(process.Killed);
    }

    [Theory]
    [InlineData(WorkshopAction.Subscribe)]
    [InlineData(WorkshopAction.Unsubscribe)]
    [InlineData(WorkshopAction.Update)]
    public async Task RunAsync_SendsTheHelperAction(WorkshopAction action)
    {
        Script(new SteamHelperStarted(), new SteamHelperItemDone(7), new SteamHelperFinished());

        ResultAssert.Ok(await Runner().RunAsync(_root, new WorkshopActionRequest(action, [7]), null, Ct));

        Assert.Equal(action.ToString(), SentRequest(Assert.Single(_processes.PipedProcesses)).Action.ToString());
    }

    [Fact]
    public async Task RunAsync_ReportsEventsAndReturnsEachItemsOutcomeInRequestOrder()
    {
        Script(
            new SteamHelperStarted(),
            new SteamHelperProgress(2, 10, 100),
            new SteamHelperProgress(2, 100, 100),
            new SteamHelperItemDone(2),
            new SteamHelperItemFailed(1, WorkshopItemFailure.DownloadFailed, 15),
            new SteamHelperFinished());
        var progress = new ListProgress<WorkshopActionEvent>();

        var outcome = ResultAssert.Ok(await Runner().RunAsync(_root, Update(1, 2), progress, Ct));

        Assert.Equal(
            [new WorkshopItemOutcome(1, WorkshopItemFailure.DownloadFailed, 15), new WorkshopItemOutcome(2, null, 0)],
            outcome.Items);
        WorkshopActionEvent[] expected =
            [
                new WorkshopActionStarted(),
                new WorkshopItemProgress(2, 10, 100),
                new WorkshopItemProgress(2, 100, 100),
                new WorkshopItemFinished(new WorkshopItemOutcome(2, null, 0)),
                new WorkshopItemFinished(new WorkshopItemOutcome(1, WorkshopItemFailure.DownloadFailed, 15)),
            ];
        Assert.Equal(expected, progress.Reports);
    }

    [Fact]
    public async Task RunAsync_ItemTheHelperNeverReported_IsNotReported()
    {
        Script(new SteamHelperStarted(), new SteamHelperItemDone(1), new SteamHelperFinished());

        var outcome = ResultAssert.Ok(await Runner().RunAsync(_root, Update(1, 2), null, Ct));

        Assert.Equal(new WorkshopItemOutcome(2, WorkshopItemFailure.NotReported, 0), outcome.Items[1]);
    }

    [Fact]
    public async Task RunAsync_DuplicateIds_AreSentOnce()
    {
        Script(new SteamHelperStarted(), new SteamHelperItemDone(1), new SteamHelperItemDone(2), new SteamHelperFinished());

        var outcome = ResultAssert.Ok(await Runner().RunAsync(_root, Update(1, 2, 1), null, Ct));

        Assert.Equal([1UL, 2UL], SentRequest(Assert.Single(_processes.PipedProcesses)).Ids);
        Assert.Equal([1UL, 2UL], outcome.Items.Select(item => item.Id));
    }

    [Fact]
    public async Task RunAsync_NoIds_SucceedsWithoutStartingTheHelper()
    {
        var outcome = ResultAssert.Ok(await Runner().RunAsync(_root, Update(), null, Ct));

        Assert.Empty(outcome.Items);
        Assert.Empty(_processes.Requests);
    }

    [Fact]
    public async Task RunAsync_StderrOutput_IsIgnored()
    {
        _processes.OnStartPiped = process =>
        {
            process.ErrorOutputTail = ["Setting breakpad minidump AppID = 108600", "SteamInternal_SetMinidumpSteamID:  Caching Steam ID:  1 [API loaded no]"];
            process.Output(Line(new SteamHelperStarted()), Line(new SteamHelperItemDone(1)), Line(new SteamHelperFinished()));
            process.Finish();
        };

        var outcome = ResultAssert.Ok(await Runner().RunAsync(_root, Update(1), null, Ct));

        Assert.Equal([new WorkshopItemOutcome(1, null, 0)], outcome.Items);
    }

    [Fact]
    public async Task RunAsync_UnreadableLines_AreSkipped()
    {
        _processes.OnStartPiped = process =>
        {
            process.Output(
                Line(new SteamHelperStarted()),
                "not json",
                "",
                """{"event":"unknownEvent"}""",
                """{"id":1}""",
                """{"event":"itemDone"}""",
                Line(new SteamHelperItemDone(1)),
                Line(new SteamHelperFinished()));
            process.Finish();
        };

        var outcome = ResultAssert.Ok(await Runner().RunAsync(_root, Update(1), null, Ct));

        Assert.Equal([new WorkshopItemOutcome(1, null, 0)], outcome.Items);
    }

    [Fact]
    public async Task RunAsync_MissingSteamApi_FailsWithoutStartingTheHelper()
    {
        File.Delete(SteamApi);

        var error = ResultAssert.Failed(await Runner().RunAsync(_root, Update(1), null, Ct), ErrorCode.WorkshopSteamApiMissing);

        Assert.Equal(_root, error.Details["path"]);
        Assert.Empty(_processes.Requests);
    }

    [Theory]
    [InlineData(true, true, "")]
    [InlineData(false, true, "natives")]
    [InlineData(true, false, "")]
    public async Task RunAsync_Linux_LoadsTheGameFoldersLibraryFirst_ThenNatives(bool inGameFolder, bool inNatives, string expectedFolder)
    {
        _platform.Os = PlatformKind.Linux;
        var root = new GameInstallBuilder(_temp.Path, PlatformKind.Linux).CreateNested("Linux", GameInstallBuilder.LinuxGameJson, withJre: false);
        var game = GameInstallBuilder.GameFolder(root, nested: true);
        if (inGameFolder)
        {
            await File.WriteAllTextAsync(Path.Combine(game, WorkshopFiles.LinuxSteamApi), "", Ct);
        }

        if (inNatives)
        {
            var natives = Directory.CreateDirectory(Path.Combine(game, PzLaunchFiles.NativesFolder)).FullName;
            await File.WriteAllTextAsync(Path.Combine(natives, WorkshopFiles.LinuxSteamApi), "", Ct);
        }

        Script(new SteamHelperStarted(), new SteamHelperItemDone(1), new SteamHelperFinished());

        ResultAssert.Ok(await Runner().RunAsync(root, Update(1), null, Ct));

        Assert.Equal(game, _processes.LastRequest.WorkingDirectory);
        Assert.Equal(Path.Combine(game, expectedFolder, WorkshopFiles.LinuxSteamApi), SentRequest(Assert.Single(_processes.PipedProcesses)).SteamApiPath);
    }

    [Fact]
    public async Task RunAsync_LinuxWithoutLibrary_FailsMissing()
    {
        _platform.Os = PlatformKind.Linux;
        var root = new GameInstallBuilder(_temp.Path, PlatformKind.Linux).CreateNested("Linux", GameInstallBuilder.LinuxGameJson, withJre: false);

        ResultAssert.Failed(await Runner().RunAsync(root, Update(1), null, Ct), ErrorCode.WorkshopSteamApiMissing);
        Assert.Empty(_processes.Requests);
    }

    [Theory]
    [InlineData(nameof(SteamHelperFailure.SteamNotRunning), ErrorCode.WorkshopSteamNotRunning)]
    [InlineData(nameof(SteamHelperFailure.InitFailed), ErrorCode.WorkshopSteamInitFailed)]
    [InlineData(nameof(SteamHelperFailure.VersionMismatch), ErrorCode.WorkshopSteamInitFailed)]
    [InlineData(nameof(SteamHelperFailure.LibraryNotLoaded), ErrorCode.WorkshopSteamApiUnusable)]
    [InlineData(nameof(SteamHelperFailure.ExportMissing), ErrorCode.WorkshopSteamApiUnusable)]
    [InlineData(nameof(SteamHelperFailure.BadRequest), ErrorCode.WorkshopHelperFailed)]
    [InlineData(nameof(SteamHelperFailure.QueryFailed), ErrorCode.WorkshopCollectionsQueryFailed)]
    public async Task RunAsync_HelperFailure_MapsToItsError(string reason, ErrorCode expected)
    {
        Script(new SteamHelperFailed(Enum.Parse<SteamHelperFailure>(reason), "steam says no"));

        ResultAssert.Failed(await Runner().RunAsync(_root, Update(1), null, Ct), expected);
    }

    [Fact]
    public async Task RunAsync_LibraryFailure_NamesTheLibrary()
    {
        Script(new SteamHelperFailed(SteamHelperFailure.ExportMissing, "SteamAPI_InitFlat"));

        var error = ResultAssert.Failed(await Runner().RunAsync(_root, Update(1), null, Ct), ErrorCode.WorkshopSteamApiUnusable);

        Assert.Equal((SteamApi, "SteamAPI_InitFlat"), (error.Details["path"], error.Details["reason"]));
    }

    [Fact]
    public async Task RunAsync_ExitWithoutFinished_Fails()
    {
        Script(new SteamHelperStarted(), new SteamHelperItemDone(1));

        ResultAssert.Failed(await Runner().RunAsync(_root, Update(1), null, Ct), ErrorCode.WorkshopHelperFailed);
    }

    [Fact]
    public async Task RunAsync_ExitWithoutAnyOutput_Fails()
    {
        _processes.OnStartPiped = process => process.Finish(-532462766);

        var error = ResultAssert.Failed(await Runner().RunAsync(_root, Update(1), null, Ct), ErrorCode.WorkshopHelperFailed);

        Assert.Contains("-532462766", error.Details["reason"], StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_NoEventWithinStallTimeout_KillsTheHelper()
    {
        _processes.OnStartPiped = process => process.Output(Line(new SteamHelperStarted()));

        ResultAssert.Failed(await Runner(new SteppingTimeProvider()).RunAsync(_root, Update(1), null, Ct), ErrorCode.WorkshopHelperTimedOut);

        var process = Assert.Single(_processes.PipedProcesses);
        Assert.True(process.Killed);
        Assert.True(process.IsDisposed);
    }

    [Fact]
    public async Task RunAsync_Cancelled_KillsTheHelperAndThrows()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        _processes.OnStartPiped = process => process.Output(Line(new SteamHelperStarted()));
        var progress = new ListProgress<WorkshopActionEvent>(_ => cts.Cancel());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Runner().RunAsync(_root, Update(1), progress, cts.Token));

        Assert.True(Assert.Single(_processes.PipedProcesses).Killed);
    }

    [Fact]
    public async Task RunAsync_HelperDoesNotStart_Fails()
    {
        _processes.StartException = new Win32Exception(2, "not found");

        var error = ResultAssert.Failed(await Runner().RunAsync(_root, Update(1), null, Ct), ErrorCode.WorkshopHelperStartFailed);

        Assert.Equal(Executable, error.Details["path"]);
    }

    [Fact]
    public async Task RunAsync_UnknownOwnExecutable_FailsToStart()
    {
        var runner = new WorkshopActionRunner(_platform, _processes, new ManualTimeProvider(), NullLogger<WorkshopActionRunner>.Instance)
        {
            HelperExecutable = null,
        };

        ResultAssert.Failed(await runner.RunAsync(_root, Update(1), null, Ct), ErrorCode.WorkshopHelperStartFailed);
        Assert.Empty(_processes.Requests);
    }

    [Fact]
    public async Task RunAsync_SecondCall_WaitsForTheRunningHelper()
    {
        var runner = Runner();
        _processes.OnStartPiped = process =>
        {
            if (_processes.PipedProcesses.Count > 1)
            {
                process.Output(Line(new SteamHelperStarted()), Line(new SteamHelperItemDone(2)), Line(new SteamHelperFinished()));
                process.Finish();
            }
        };

        var first = runner.RunAsync(_root, Update(1), null, Ct);
        var second = runner.RunAsync(_root, Update(2), null, Ct);

        Assert.Single(_processes.Requests);
        Assert.False(second.IsCompleted);
        var running = _processes.PipedProcesses[0];
        running.Output(Line(new SteamHelperStarted()), Line(new SteamHelperItemDone(1)), Line(new SteamHelperFinished()));
        running.Finish();
        Assert.Equal(1UL, Assert.Single(ResultAssert.Ok(await first).Items).Id);
        Assert.Equal(2UL, Assert.Single(ResultAssert.Ok(await second).Items).Id);
        Assert.Equal(2, _processes.Requests.Count);
    }

    [Fact]
    public async Task RunAsync_RelativeInstallRoot_IsInvalid()
    {
        ResultAssert.Failed(await Runner().RunAsync("relative", Update(1), null, Ct), ErrorCode.InstallInvalidPath);
    }

    [Fact]
    public async Task ListCollectionsAsync_StartsTheHelperWithTheListRequest_AndReturnsItsCollections()
    {
        var listed = new WorkshopUserCollections(
            12345678,
            [new WorkshopCollectionSummary(3623026433, "Pack", 201), new WorkshopCollectionSummary(2690796635, null, 71)],
            [new WorkshopCollectionSummary(3815132834, "Mine", 214)]);
        Script(new SteamHelperStarted(), new SteamHelperCollections(listed), new SteamHelperFinished());

        var collections = ResultAssert.Ok(await Runner().ListCollectionsAsync(_root, Ct));

        Assert.Equal(listed.AccountId, collections.AccountId);
        Assert.Equal(listed.Favorited, collections.Favorited);
        Assert.Equal(listed.Published, collections.Published);
        var start = Assert.Single(_processes.Requests);
        Assert.Equal([LauncherArguments.SteamHelper], start.Arguments);
        Assert.Equal("108600", start.Environment["SteamAppId"]);
        var process = Assert.Single(_processes.PipedProcesses);
        var sent = SentRequest(process);
        Assert.Equal((SteamHelperAction.ListCollections, SteamApi), (sent.Action, sent.SteamApiPath));
        Assert.Empty(sent.Ids);
        Assert.True(process.IsDisposed);
        Assert.False(process.Killed);
    }

    [Fact]
    public async Task ListCollectionsAsync_FinishedWithoutCollections_Fails()
    {
        Script(new SteamHelperStarted(), new SteamHelperFinished());

        ResultAssert.Failed(await Runner().ListCollectionsAsync(_root, Ct), ErrorCode.WorkshopHelperFailed);
    }

    [Fact]
    public async Task ListCollectionsAsync_CollectionsWithoutFinished_Fails()
    {
        Script(new SteamHelperStarted(), new SteamHelperCollections(new WorkshopUserCollections(1, [], [])));

        ResultAssert.Failed(await Runner().ListCollectionsAsync(_root, Ct), ErrorCode.WorkshopHelperFailed);
    }

    [Theory]
    [InlineData(nameof(SteamHelperFailure.SteamNotRunning), ErrorCode.WorkshopSteamNotRunning)]
    [InlineData(nameof(SteamHelperFailure.InitFailed), ErrorCode.WorkshopSteamInitFailed)]
    [InlineData(nameof(SteamHelperFailure.ExportMissing), ErrorCode.WorkshopSteamApiUnusable)]
    [InlineData(nameof(SteamHelperFailure.QueryFailed), ErrorCode.WorkshopCollectionsQueryFailed)]
    public async Task ListCollectionsAsync_HelperFailure_MapsToItsError(string reason, ErrorCode expected)
    {
        Script(new SteamHelperFailed(Enum.Parse<SteamHelperFailure>(reason), "Favorited page 2: EResult 2"));

        ResultAssert.Failed(await Runner().ListCollectionsAsync(_root, Ct), expected);
    }

    [Fact]
    public async Task ListCollectionsAsync_QueryFailed_KeepsSteamsReason()
    {
        Script(new SteamHelperStarted(), new SteamHelperFailed(SteamHelperFailure.QueryFailed, "Favorited page 2: EResult 2"));

        var error = ResultAssert.Failed(await Runner().ListCollectionsAsync(_root, Ct), ErrorCode.WorkshopCollectionsQueryFailed);

        Assert.Equal("Favorited page 2: EResult 2", error.Details["reason"]);
    }

    [Fact]
    public async Task ListCollectionsAsync_MissingSteamApi_FailsWithoutStartingTheHelper()
    {
        File.Delete(SteamApi);

        ResultAssert.Failed(await Runner().ListCollectionsAsync(_root, Ct), ErrorCode.WorkshopSteamApiMissing);
        Assert.Empty(_processes.Requests);
    }

    [Fact]
    public async Task ListCollectionsAsync_RelativeInstallRoot_IsInvalid()
    {
        ResultAssert.Failed(await Runner().ListCollectionsAsync("relative", Ct), ErrorCode.InstallInvalidPath);
        Assert.Empty(_processes.Requests);
    }

    [Fact]
    public async Task ListCollectionsAsync_NoEventWithinStallTimeout_KillsTheHelper()
    {
        _processes.OnStartPiped = process => process.Output(Line(new SteamHelperStarted()));

        ResultAssert.Failed(await Runner(new SteppingTimeProvider()).ListCollectionsAsync(_root, Ct), ErrorCode.WorkshopHelperTimedOut);

        Assert.True(Assert.Single(_processes.PipedProcesses).Killed);
    }

    [Fact]
    public async Task ListCollectionsAsync_WhileAnActionRuns_WaitsForIt()
    {
        var runner = Runner();
        _processes.OnStartPiped = process =>
        {
            if (_processes.PipedProcesses.Count > 1)
            {
                process.Output(
                    Line(new SteamHelperStarted()), Line(new SteamHelperCollections(new WorkshopUserCollections(1, [], []))), Line(new SteamHelperFinished()));
                process.Finish();
            }
        };

        var action = runner.RunAsync(_root, Update(1), null, Ct);
        var list = runner.ListCollectionsAsync(_root, Ct);

        Assert.Single(_processes.Requests);
        Assert.False(list.IsCompleted);
        var running = _processes.PipedProcesses[0];
        running.Output(Line(new SteamHelperStarted()), Line(new SteamHelperItemDone(1)), Line(new SteamHelperFinished()));
        running.Finish();
        ResultAssert.Ok(await action);
        Assert.Equal(1u, ResultAssert.Ok(await list).AccountId);
        Assert.Equal(SteamHelperAction.ListCollections, SentRequest(_processes.PipedProcesses[1]).Action);
    }
}
