using PzLauncher.Core.Common;
using PzLauncher.Core.Launch;
using PzLauncher.Desktop.Lang;
using PzLauncher.Desktop.Services.Launch;
using PzLauncher.Desktop.Services.Preferences;
using PzLauncher.Desktop.Services.Shell.Dialogs;
using PzLauncher.Desktop.Services.Shell;
using PzLauncher.Desktop.Tests.TestSupport;

namespace PzLauncher.Desktop.Tests.Services.Launch;

[Collection(UiTextCultureCollection.Name)]
public class AfterLaunchFlowTests : UiTextCultureTest
{
    private readonly FakePreferenceStore _preferences = new();
    private readonly FakeDialogService _dialogs = new();
    private readonly FakeLauncherWindow _window = new();
    private readonly FakeAppLifetime _lifetime = new();
    private readonly FakeGameLaunchService _launcher = new();
    private readonly AfterLaunchSettings _settings;
    private readonly AfterLaunchFlow _flow;

    public AfterLaunchFlowTests()
    {
        _settings = new AfterLaunchSettings(_preferences, new UserPreferences(null, null, null), new FakePlatformEnvironment());
        _flow = new AfterLaunchFlow(_settings, _window, _lifetime, _dialogs, _launcher);
    }

    public static TheoryData<AfterLaunchAction> Actions => [.. Enum.GetValues<AfterLaunchAction>()];

    private Task StartGameAsync() =>
        _launcher.LaunchAsync(new LaunchGameRequest(TestInstalls.SteamGame.RootPath, GameLaunchOverrides.Empty));

    [Theory]
    [MemberData(nameof(Actions))]
    public async Task ChooseAsync_Saved_ReturnsItWithoutAsking(AfterLaunchAction saved)
    {
        _settings.Action = saved;

        Assert.Equal(saved, await _flow.ChooseAsync());

        Assert.Empty(_dialogs.SingleChoices);
    }

    [Fact]
    public async Task ChooseAsync_NothingSaved_AsksWithTheOfferedChoices_KeepPreselected()
    {
        await _flow.ChooseAsync();

        var prompt = Assert.IsType<ChoicePrompt<AfterLaunchAction>>(Assert.Single(_dialogs.SingleChoices));
        Assert.Equal(UiText.AfterLaunch_PromptTitle, prompt.Title);
        Assert.Equal(UiText.AfterLaunch_PromptMessage, prompt.Message);
        Assert.Equal(UiText.AfterLaunch_PromptHint, prompt.Hint);
        Assert.Equal(_settings.Options(), prompt.Options);
        Assert.Equal(AfterLaunchAction.Keep, prompt.Preselected);
    }

    [Fact]
    public async Task ChooseAsync_Answered_SavesTheChoiceAndNeverAsksAgain()
    {
        _dialogs.SingleChoiceAnswers.Enqueue(AfterLaunchAction.Minimize);

        Assert.Equal(AfterLaunchAction.Minimize, await _flow.ChooseAsync());
        Assert.Equal(AfterLaunchAction.Minimize, await _flow.ChooseAsync());

        Assert.Single(_dialogs.SingleChoices);
        Assert.Equal(AfterLaunchAction.Minimize, _settings.Action);
        Assert.Equal([AfterLaunchAction.Minimize], _preferences.SavedAfterLaunch);
    }

    [Fact]
    public async Task ChooseAsync_Dismissed_KeepsThisTime_SavesNothingAndAsksAgain()
    {
        Assert.Equal(AfterLaunchAction.Keep, await _flow.ChooseAsync());
        Assert.Equal(AfterLaunchAction.Keep, await _flow.ChooseAsync());

        Assert.Equal(2, _dialogs.SingleChoices.Count);
        Assert.Null(_settings.Action);
        Assert.Empty(_preferences.SavedAfterLaunch);
    }

    public static TheoryData<AfterLaunchAction, string[]> WindowWhileRunning => new()
    {
        { AfterLaunchAction.Keep, [] },
        { AfterLaunchAction.Minimize, [nameof(FakeLauncherWindow.Minimize)] },
        { AfterLaunchAction.MinimizeToTray, [nameof(FakeLauncherWindow.Hide)] },
        { AfterLaunchAction.Close, [nameof(FakeLauncherWindow.Hide)] },
    };

    [Theory]
    [MemberData(nameof(WindowWhileRunning))]
    public async Task WatchAsync_GameExits_ChangesTheWindowOnStartAndRestoresItUnlessKept(AfterLaunchAction action, string[] onStart)
    {
        await StartGameAsync();
        var crash = TestErrors.Create(ErrorCode.LaunchGameCrashed, "hs_err_pid4242.log");

        var watch = _flow.WatchAsync(action);

        Assert.Equal(onStart, _window.Calls);
        Assert.False(watch.IsCompleted);
        _launcher.Exit.SetResult(crash);
        Assert.Equal(crash, await watch);
        string[] expected = action == AfterLaunchAction.Keep ? [] : [.. onStart, nameof(FakeLauncherWindow.Restore)];
        Assert.Equal(expected, _window.Calls);
        Assert.Equal(0, _lifetime.CloseCalls);
    }

    [Fact]
    public async Task WatchAsync_CloseAndTheGameOutlivesTheEarlyExitWindow_ClosesTheLauncherAndStillWaitsForTheExit()
    {
        await StartGameAsync();

        var watch = _flow.WatchAsync(AfterLaunchAction.Close);

        Assert.Equal(0, _lifetime.CloseCalls);
        _launcher.PastEarlyExit.SetResult(true);
        Assert.Equal(1, _lifetime.CloseCalls);
        Assert.False(watch.IsCompleted);
        _launcher.Exit.SetResult(Result.Ok());
        Assert.Null(await watch);
        Assert.Equal([nameof(FakeLauncherWindow.Hide)], _window.Calls);
    }

    [Fact]
    public async Task WatchAsync_CloseAndTheGameExitsEarly_StaysOpenAndRestores()
    {
        await StartGameAsync();
        var exitedEarly = TestErrors.Create(ErrorCode.LaunchGameExitedEarly, "Unrecognized VM option 'UseShenandoahGC'");

        var watch = _flow.WatchAsync(AfterLaunchAction.Close);
        _launcher.Exit.SetResult(exitedEarly);

        Assert.Equal(exitedEarly, await watch);
        Assert.Equal(0, _lifetime.CloseCalls);
        Assert.Equal([nameof(FakeLauncherWindow.Hide), nameof(FakeLauncherWindow.Restore)], _window.Calls);
    }

    [Fact]
    public async Task WatchAsync_CloseAndTheWindowBroughtBackDuringTheWait_StaysOpen()
    {
        await StartGameAsync();

        var watch = _flow.WatchAsync(AfterLaunchAction.Close);
        _window.Restore();
        _launcher.PastEarlyExit.SetResult(true);
        _launcher.Exit.SetResult(Result.Ok());

        Assert.Equal(Result.Ok(), await watch);
        Assert.Equal(0, _lifetime.CloseCalls);
    }
}
