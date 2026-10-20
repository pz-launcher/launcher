using PzLauncher.Core.Common;
using PzLauncher.Core.Launch;
using PzLauncher.Desktop.Lang;
using PzLauncher.Desktop.Localization;
using PzLauncher.Desktop.Services.Launch;
using PzLauncher.Desktop.Services.Preferences;
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
        _settings = new AfterLaunchSettings(_preferences, new UserPreferences(null, null, null));
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
    public async Task ChooseAsync_NothingSaved_AsksWithEveryChoiceInOrder_KeepPreselected()
    {
        await _flow.ChooseAsync();

        var prompt = Assert.IsType<ChoicePrompt<AfterLaunchAction>>(Assert.Single(_dialogs.SingleChoices));
        Assert.Equal(UiText.AfterLaunch_PromptTitle, prompt.Title);
        Assert.Equal(UiText.AfterLaunch_PromptMessage, prompt.Message);
        Assert.Equal(UiText.AfterLaunch_PromptHint, prompt.Hint);
        Assert.Equal(
            [AfterLaunchAction.Keep, AfterLaunchAction.Minimize, AfterLaunchAction.MinimizeToTray, AfterLaunchAction.Close],
            prompt.Options.Select(option => option.Value));
        Assert.Equal(prompt.Options.Select(option => DisplayNames.Of(option.Value)), prompt.Options.Select(option => option.Text));
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
    public void Apply_Chosen_ChangesTheWindow(AfterLaunchAction action, string[] expected)
    {
        _flow.Apply(action);

        Assert.Equal(expected, _window.Calls);
    }

    [Theory]
    [MemberData(nameof(Actions))]
    public void Restore_Chosen_BringsTheWindowBackUnlessKept(AfterLaunchAction action)
    {
        _flow.Restore(action);

        string[] expected = action == AfterLaunchAction.Keep ? [] : [nameof(FakeLauncherWindow.Restore)];
        Assert.Equal(expected, _window.Calls);
    }

    [Theory]
    [InlineData(AfterLaunchAction.Keep)]
    [InlineData(AfterLaunchAction.Minimize)]
    [InlineData(AfterLaunchAction.MinimizeToTray)]
    public async Task CloseWhenStartedAsync_NotClose_ReturnsFalseAtOnce(AfterLaunchAction action)
    {
        await StartGameAsync();
        _flow.Apply(action);

        var close = _flow.CloseWhenStartedAsync(action);

        Assert.True(close.IsCompleted);
        Assert.False(await close);
        Assert.Equal(0, _lifetime.CloseCalls);
    }

    [Fact]
    public async Task CloseWhenStartedAsync_GameOutlivesTheEarlyExitWindow_ClosesTheLauncher()
    {
        await StartGameAsync();
        _flow.Apply(AfterLaunchAction.Close);

        var close = _flow.CloseWhenStartedAsync(AfterLaunchAction.Close);

        Assert.False(close.IsCompleted);
        _launcher.PastEarlyExit.SetResult(true);
        Assert.True(await close);
        Assert.Equal(1, _lifetime.CloseCalls);
        Assert.Equal([nameof(FakeLauncherWindow.Hide)], _window.Calls);
    }

    [Fact]
    public async Task CloseWhenStartedAsync_GameExitsEarly_StaysOpen()
    {
        await StartGameAsync();
        _flow.Apply(AfterLaunchAction.Close);

        var close = _flow.CloseWhenStartedAsync(AfterLaunchAction.Close);
        _launcher.Exit.SetResult(TestErrors.Create(ErrorCode.LaunchGameExitedEarly, "Unrecognized VM option 'UseShenandoahGC'"));

        Assert.False(await close);
        Assert.Equal(0, _lifetime.CloseCalls);
    }

    [Fact]
    public async Task CloseWhenStartedAsync_WindowBroughtBackDuringTheWait_StaysOpen()
    {
        await StartGameAsync();
        _flow.Apply(AfterLaunchAction.Close);

        var close = _flow.CloseWhenStartedAsync(AfterLaunchAction.Close);
        _window.Restore();
        _launcher.PastEarlyExit.SetResult(true);

        Assert.False(await close);
        Assert.Equal(0, _lifetime.CloseCalls);
    }
}
