using PzLauncher.Core.Common;
using PzLauncher.Core.ZombieBuddy;
using PzLauncher.Desktop.Localization;
using PzLauncher.Desktop.Tests.TestSupport;
using PzLauncher.Desktop.ViewModels.ZombieBuddy;
using static PzLauncher.Desktop.Tests.TestSupport.TestInstalls;
using static PzLauncher.Desktop.Tests.TestSupport.TestZombieBuddy;

namespace PzLauncher.Desktop.Tests.ViewModels.ZombieBuddy;

[Collection(UiTextCultureCollection.Name)]
public class ZombieBuddySetupViewModelTests : UiTextCultureTest
{
    private readonly FakeZombieBuddyService _service = new();
    private readonly List<bool> _closeRequests = [];

    private ZombieBuddySetupViewModel Create(ZombieBuddySetupMode mode = ZombieBuddySetupMode.Initialize, bool offersLaunch = true)
    {
        var vm = new ZombieBuddySetupViewModel(_service, mode, SteamGame.RootPath, ZombieBuddyChannel.Beta, offersLaunch);
        vm.CloseRequested += (_, launch) => _closeRequests.Add(launch);
        return vm;
    }

    private static ZombieBuddyStepState[] States(ZombieBuddySetupViewModel vm) => [.. vm.Steps.Select(row => row.State)];

    [Fact]
    public async Task Start_Initialize_RunsForTheInstallAndChannel()
    {
        var vm = Create();

        await vm.StartAsync();

        Assert.Equal([(SteamGame.RootPath, ZombieBuddyChannel.Beta)], _service.Initializations);
        Assert.Empty(_service.Removals);
    }

    [Fact]
    public async Task Start_Remove_RunsRemove()
    {
        _service.Operation = (progress, _) => Task.FromResult(Run(RemoveSteps, progress));
        var vm = Create(ZombieBuddySetupMode.Remove);

        await vm.StartAsync();

        Assert.Equal([SteamGame.RootPath], _service.Removals);
        Assert.Empty(_service.Initializations);
        Assert.Equal(RemoveSteps, vm.Steps.Select(row => row.Step));
        Assert.NotEqual(Create().Title, vm.Title);
    }

    [Fact]
    public async Task Start_Succeeds_EveryStepDoneAndLaunchEnabled()
    {
        var vm = Create();

        await vm.StartAsync();

        Assert.Equal(InitializeSteps, vm.Steps.Select(row => row.Step));
        Assert.All(vm.Steps, row => Assert.Equal(ZombieBuddyStepState.Done, row.State));
        Assert.Equal(1, vm.Progress);
        Assert.True(vm.IsSucceeded);
        Assert.False(vm.IsRunning);
        Assert.False(vm.IsFailed);
        Assert.Null(vm.ErrorText);
        Assert.True(vm.LaunchCommand.CanExecute(null));
        Assert.False(vm.RetryCommand.CanExecute(null));
    }

    [Fact]
    public async Task Start_Running_StepsFollowTheReportedStep()
    {
        var gate = new TaskCompletionSource<Result>();
        _service.Operation = (progress, _) =>
        {
            progress?.Report(new ZombieBuddyProgress(InitializeSteps, 1, IsComplete: false));
            return gate.Task;
        };
        var vm = Create();

        var start = vm.StartAsync();

        Assert.Equal([ZombieBuddyStepState.Done, ZombieBuddyStepState.Running, ZombieBuddyStepState.Pending, ZombieBuddyStepState.Pending], States(vm));
        Assert.Equal(0.25, vm.Progress);
        Assert.True(vm.IsRunning);
        Assert.False(vm.LaunchCommand.CanExecute(null));
        Assert.False(vm.RetryCommand.CanExecute(null));
        Assert.True(vm.CancelCommand.CanExecute(null));

        gate.SetResult(Result.Ok());
        await start;

        Assert.True(vm.IsSucceeded);
    }

    [Fact]
    public async Task Start_StepFails_MarksItFailedShowsTheReasonAndOffersRetry()
    {
        var error = TestErrors.Create(ErrorCode.ZombieBuddyCopyFailed, @"E:\SteamLibrary\steamapps\common\ProjectZomboid\ZombieBuddy.jar");
        _service.Operation = (progress, _) => Task.FromResult(Run(InitializeSteps, progress, failAt: 2, error));
        var vm = Create();

        await vm.StartAsync();

        Assert.Equal([ZombieBuddyStepState.Done, ZombieBuddyStepState.Done, ZombieBuddyStepState.Failed, ZombieBuddyStepState.Pending], States(vm));
        Assert.Equal(ErrorMessages.ToUserText(error), vm.ErrorText);
        Assert.True(vm.IsFailed);
        Assert.False(vm.IsSucceeded);
        Assert.False(vm.LaunchCommand.CanExecute(null));
        Assert.True(vm.RetryCommand.CanExecute(null));
    }

    [Fact]
    public async Task Retry_AfterAFailure_RunsTheWholeOperationAgain()
    {
        var attempts = 0;
        _service.Operation = (progress, _) => Task.FromResult(Run(InitializeSteps, progress, failAt: ++attempts == 1 ? 1 : null));
        var vm = Create();
        await vm.StartAsync();

        await vm.RetryCommand.ExecuteAsync(null);

        Assert.Equal(2, _service.Initializations.Count);
        Assert.All(vm.Steps, row => Assert.Equal(ZombieBuddyStepState.Done, row.State));
        Assert.Null(vm.ErrorText);
        Assert.True(vm.IsSucceeded);
        Assert.False(vm.IsFailed);
    }

    [Fact]
    public async Task Start_FailsBeforeAnyStep_ShowsTheReasonWithoutSteps()
    {
        var error = TestErrors.Create(ErrorCode.InstallPathNotFound, SteamGame.RootPath);
        _service.Operation = (_, _) => Task.FromResult<Result>(error);
        var vm = Create();

        await vm.StartAsync();

        Assert.Empty(vm.Steps);
        Assert.Equal(ErrorMessages.ToUserText(error), vm.ErrorText);
        Assert.True(vm.IsFailed);
    }

    [Fact]
    public async Task Start_Twice_RunsOnce()
    {
        var vm = Create();

        await vm.StartAsync();
        await vm.StartAsync();

        Assert.Single(_service.Initializations);
    }

    [Fact]
    public async Task Cancel_WhileRunning_CancelsTheOperationAndClosesWithoutLaunch()
    {
        var token = CancellationToken.None;
        _service.Operation = async (progress, ct) =>
        {
            token = ct;
            progress?.Report(new ZombieBuddyProgress(InitializeSteps, 0, IsComplete: false));
            await Task.Delay(Timeout.Infinite, ct);
            return Result.Ok();
        };
        var vm = Create();
        var start = vm.StartAsync();

        vm.CancelCommand.Execute(null);
        await start;

        Assert.True(token.IsCancellationRequested);
        Assert.Equal([false], _closeRequests);
        Assert.False(vm.IsSucceeded);
    }

    [Fact]
    public async Task Abort_WhileRunning_CancelsWithoutACloseRequest()
    {
        var token = CancellationToken.None;
        _service.Operation = async (_, ct) =>
        {
            token = ct;
            await Task.Delay(Timeout.Infinite, ct);
            return Result.Ok();
        };
        var vm = Create();
        var start = vm.StartAsync();

        vm.Abort();
        await start;

        Assert.True(token.IsCancellationRequested);
        Assert.Empty(_closeRequests);
    }

    [Fact]
    public async Task Launch_AfterSuccess_RequestsCloseWithLaunch()
    {
        var vm = Create();
        await vm.StartAsync();

        vm.LaunchCommand.Execute(null);

        Assert.True(vm.OffersLaunch);
        Assert.Equal((false, true), (vm.OffersConfirm, vm.OffersCancel));
        Assert.Equal([true], _closeRequests);
    }

    [Fact]
    public async Task Confirm_CloseOnlyDialogAfterSuccess_ReplacesCancelAndRequestsCloseWithoutLaunch()
    {
        var vm = Create(offersLaunch: false);
        Assert.False(vm.ConfirmCommand.CanExecute(null));
        Assert.Equal((false, true), (vm.OffersConfirm, vm.OffersCancel));
        await vm.StartAsync();

        Assert.Equal((true, false), (vm.OffersConfirm, vm.OffersCancel));
        vm.ConfirmCommand.Execute(null);

        Assert.False(vm.OffersLaunch);
        Assert.Equal([false], _closeRequests);
    }

    [Fact]
    public async Task Start_CloseOnlyDialogStepFails_KeepsCancel()
    {
        var error = TestErrors.Create(ErrorCode.ZombieBuddyCopyFailed);
        _service.Operation = (progress, _) => Task.FromResult(Run(InitializeSteps, progress, failAt: 1, error));
        var vm = Create(offersLaunch: false);

        await vm.StartAsync();

        Assert.Equal((false, true), (vm.OffersConfirm, vm.OffersCancel));
    }
}
