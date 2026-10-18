using PzLauncher.Core.Common;
using PzLauncher.Core.Steam;
using PzLauncher.Desktop.Lang;
using PzLauncher.Desktop.Localization;
using PzLauncher.Desktop.Tests.TestSupport;

namespace PzLauncher.Desktop.Tests.Services.Steam;

[Collection(UiTextCultureCollection.Name)]
public class SteamLaunchHookFlowTests : UiTextCultureTest
{
    private readonly FakeSteamLaunchHookService _hooks = new();
    private readonly FakeDialogService _dialogs = new();

    private Task<Result<SteamLaunchHookStatus>> SetAsync(bool enabled) => TestSteam.Flow(_hooks, _dialogs).SetAsync(enabled);

    [Fact]
    public async Task SetAsync_SteamClosed_SetsWithoutAskingOrRestarting()
    {
        var result = await SetAsync(enabled: true);

        Assert.Equal([(TestSteam.LauncherExe, true, false)], _hooks.Sets);
        Assert.Empty(_dialogs.Confirms);
        Assert.Equal(SteamLaunchHookState.On, Assert.IsType<SteamLaunchHookStatus>(result.Value).State);
    }

    [Fact]
    public async Task SetAsync_SteamRunningConfirmed_RestartsSteam()
    {
        _hooks.Current = TestSteam.Status(SteamLaunchHookState.On, isSteamRunning: true);
        _dialogs.ConfirmAnswers.Enqueue(true);

        await SetAsync(enabled: false);

        Assert.Equal(UiText.Steam_ConfirmApply, Assert.Single(_dialogs.Confirms).ConfirmText);
        Assert.Equal([(TestSteam.LauncherExe, false, true)], _hooks.Sets);
    }

    [Fact]
    public async Task SetAsync_SteamRunningCancelled_ChangesNothing()
    {
        _hooks.Current = TestSteam.Status(SteamLaunchHookState.Off, isSteamRunning: true);

        var result = await SetAsync(enabled: true);

        Assert.Empty(_hooks.Sets);
        Assert.Equal(_hooks.Current, result.Value);
    }

    [Fact]
    public async Task SetAsync_SetFails_ShowsTheErrorAndReturnsTheStateReadAgain()
    {
        var error = TestErrors.Create(ErrorCode.SteamShutdownTimedOut);
        _hooks.SetError = error;

        var result = await SetAsync(enabled: true);

        Assert.Equal([ErrorMessages.ToUserText(error)], _dialogs.Errors);
        Assert.Equal(2, _hooks.StatusRequests.Count);
        Assert.Equal(SteamLaunchHookState.Off, Assert.IsType<SteamLaunchHookStatus>(result.Value).State);
    }

    [Fact]
    public async Task SetAsync_Warnings_AreShownWithTheNewState()
    {
        var warning = TestErrors.Create(ErrorCode.SteamStartFailed, @"C:\Steam\steam.exe");
        _hooks.SetWarnings = [warning];

        var result = await SetAsync(enabled: true);

        Assert.Equal([ErrorMessages.ToUserText(warning)], _dialogs.Errors);
        Assert.Equal(SteamLaunchHookState.On, Assert.IsType<SteamLaunchHookStatus>(result.Value).State);
    }

    [Fact]
    public async Task SetAsync_StatusUnreadable_ShowsTheErrorAndSetsNothing()
    {
        var error = TestErrors.Create(ErrorCode.SteamLocalConfigMalformed, @"C:\Steam\userdata\1\config\localconfig.vdf");
        _hooks.StatusError = error;

        var result = await SetAsync(enabled: true);

        Assert.Equal(ErrorCode.SteamLocalConfigMalformed, result.Error?.Code);
        Assert.Equal([ErrorMessages.ToUserText(error)], _dialogs.Errors);
        Assert.Empty(_hooks.Sets);
    }
}
