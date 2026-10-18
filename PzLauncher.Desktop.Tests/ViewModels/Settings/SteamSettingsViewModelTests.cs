using PzLauncher.Core.Common;
using PzLauncher.Core.Platform;
using PzLauncher.Core.Steam;
using PzLauncher.Desktop.Lang;
using PzLauncher.Desktop.Localization;
using PzLauncher.Desktop.Services.Preferences;
using PzLauncher.Desktop.Services.Shell;
using PzLauncher.Desktop.Tests.TestSupport;
using PzLauncher.Desktop.ViewModels.Settings;

namespace PzLauncher.Desktop.Tests.ViewModels.Settings;

[Collection(UiTextCultureCollection.Name)]
public class SteamSettingsViewModelTests : UiTextCultureTest
{
    private readonly FakeSteamLaunchHookService _hooks = new();
    private readonly FakeDialogService _dialogs = new();
    private readonly FakePreferenceStore _store = new();

    private SteamSettingsViewModel Create(bool? reminder = null, PlatformKind os = PlatformKind.Windows) =>
        new(
            TestSteam.Flow(_hooks, _dialogs),
            _store,
            new UserPreferences(null, null, null) { SteamHookReminder = reminder },
            _dialogs,
            new FakePlatformEnvironment { Os = os },
            Localization);

    private async Task<SteamSettingsViewModel> LoadedAsync()
    {
        var vm = Create();
        await vm.LoadAsync();
        return vm;
    }

    [Fact]
    public void Constructor_ReadsNothingAndRemindsByDefault()
    {
        var vm = Create();

        Assert.True(vm.IsSupported);
        Assert.False(vm.IsAvailable);
        Assert.True(vm.RemindAtStartup);
        Assert.Empty(_hooks.StatusRequests);
        Assert.Empty(_store.SavedSteamHookReminders);
    }

    [Theory]
    [InlineData(PlatformKind.Linux)]
    [InlineData(PlatformKind.MacOS)]
    public async Task NotWindows_IsUnsupportedAndNeverReadsOrReminds(PlatformKind os)
    {
        var vm = Create(os: os);

        await vm.LoadAsync();
        await vm.RemindAtStartupAsync();

        Assert.False(vm.IsSupported);
        Assert.Empty(_hooks.StatusRequests);
        Assert.Empty(_dialogs.OptionConfirms);
    }

    [Fact]
    public async Task LoadAsync_HookOff_ShowsTheAccountAndAnEnabledSwitch()
    {
        var vm = await LoadedAsync();

        Assert.Equal("Steam account: Survivor", vm.AccountText);
        Assert.False(vm.IsHookOn);
        Assert.True(vm.CanToggle);
        Assert.Null(vm.OtherLauncherText);
        Assert.Null(vm.StatusText);
        Assert.Empty(_hooks.Sets);
    }

    [Fact]
    public async Task LoadAsync_HookOn_ShowsTheSwitchOnWithoutApplying()
    {
        _hooks.Current = TestSteam.Status(SteamLaunchHookState.On);

        var vm = await LoadedAsync();

        Assert.True(vm.IsHookOn);
        Assert.Empty(_hooks.Sets);
    }

    [Fact]
    public async Task LoadAsync_OtherLauncher_ShowsItsPathWithTheSwitchOff()
    {
        _hooks.Current = TestSteam.Status(SteamLaunchHookState.OtherLauncher);

        var vm = await LoadedAsync();

        Assert.False(vm.IsHookOn);
        Assert.Contains(TestSteam.OtherLauncherExe, vm.OtherLauncherText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadAsync_Unavailable_ShowsWhyAndDisablesTheSwitch()
    {
        var error = TestErrors.Create(ErrorCode.SteamAppNotInLocalConfig, @"C:\Steam\userdata\1\config\localconfig.vdf");
        _hooks.StatusError = error;

        var vm = await LoadedAsync();

        Assert.False(vm.IsAvailable);
        Assert.False(vm.CanToggle);
        Assert.Null(vm.AccountText);
        Assert.Equal(ErrorMessages.ToUserText(error), vm.StatusText);
        Assert.True(vm.IsStatusError);
    }

    [Fact]
    public async Task LoadAsync_AfterTheStateChangedInSteam_ShowsTheNewState()
    {
        var vm = await LoadedAsync();
        _hooks.Current = TestSteam.Status(SteamLaunchHookState.On);

        await vm.LoadAsync();

        Assert.True(vm.IsHookOn);
        Assert.Equal(2, _hooks.StatusRequests.Count);
    }

    [Fact]
    public async Task IsHookOn_Toggled_AppliesAndShowsTheStateFromSteam()
    {
        var vm = await LoadedAsync();
        _hooks.SetGate = new TaskCompletionSource();

        vm.IsHookOn = true;

        Assert.Equal([(TestSteam.LauncherExe, true, false)], _hooks.Sets);
        Assert.True(vm.IsHookOn);
        Assert.True(vm.IsBusy);
        Assert.False(vm.CanToggle);
        Assert.Equal(UiText.Steam_Applying, vm.StatusText);

        var idle = new TaskCompletionSource();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SteamSettingsViewModel.IsBusy) && !vm.IsBusy)
            {
                idle.TrySetResult();
            }
        };
        _hooks.SetGate.SetResult();
        await idle.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.True(vm.IsHookOn);
        Assert.False(vm.IsBusy);
        Assert.Null(vm.StatusText);
    }

    [Fact]
    public async Task IsHookOn_ToggledThenCancelled_ShowsTheRealStateAgain()
    {
        _hooks.Current = TestSteam.Status(SteamLaunchHookState.Off, isSteamRunning: true);
        var vm = await LoadedAsync();

        vm.IsHookOn = true;

        Assert.Single(_dialogs.Confirms);
        Assert.Empty(_hooks.Sets);
        Assert.False(vm.IsHookOn);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(false, false)]
    public void RemindAtStartup_ShowsTheSavedChoice(bool? saved, bool expected) => Assert.Equal(expected, Create(saved).RemindAtStartup);

    [Fact]
    public void RemindAtStartup_Changed_IsSaved()
    {
        var vm = Create();

        vm.RemindAtStartup = false;
        vm.RemindAtStartup = true;

        Assert.Equal([false, true], _store.SavedSteamHookReminders);
    }

    [Theory]
    [InlineData(SteamLaunchHookState.Off)]
    [InlineData(SteamLaunchHookState.OtherLauncher)]
    public async Task RemindAtStartupAsync_HookNotOnConfirmed_TurnsItOn(SteamLaunchHookState state)
    {
        _hooks.Current = TestSteam.Status(state);
        _dialogs.OptionAnswers.Enqueue(new ConfirmOptionResult(Confirmed: true, OptionChecked: false));
        var vm = Create();

        await vm.RemindAtStartupAsync();

        var (prompt, option) = Assert.Single(_dialogs.OptionConfirms);
        Assert.Equal((UiText.Steam_ReminderEnable, UiText.Steam_ReminderLater, UiText.Steam_ReminderDontRemind), (prompt.ConfirmText, prompt.CancelText, option));
        Assert.Equal([(TestSteam.LauncherExe, true, false)], _hooks.Sets);
        Assert.True(vm.IsHookOn);
        Assert.Empty(_store.SavedSteamHookReminders);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RemindAtStartupAsync_DontRemindChecked_IsSavedHoweverItCloses(bool confirmed)
    {
        _dialogs.OptionAnswers.Enqueue(new ConfirmOptionResult(confirmed, OptionChecked: true));
        var vm = Create();

        await vm.RemindAtStartupAsync();

        Assert.Equal([false], _store.SavedSteamHookReminders);
        Assert.False(vm.RemindAtStartup);
        Assert.Equal(confirmed ? 1 : 0, _hooks.Sets.Count);
    }

    [Fact]
    public async Task RemindAtStartupAsync_Later_ChangesNothing()
    {
        var vm = Create();

        await vm.RemindAtStartupAsync();

        Assert.Single(_dialogs.OptionConfirms);
        Assert.Empty(_hooks.Sets);
        Assert.Empty(_store.SavedSteamHookReminders);
    }

    [Fact]
    public async Task RemindAtStartupAsync_HookOn_DoesNotAsk()
    {
        _hooks.Current = TestSteam.Status(SteamLaunchHookState.On);

        await Create().RemindAtStartupAsync();

        Assert.Empty(_dialogs.OptionConfirms);
    }

    [Fact]
    public async Task RemindAtStartupAsync_StatusUnavailable_DoesNotAskOrShowAnError()
    {
        _hooks.StatusError = TestErrors.Create(ErrorCode.SteamNotInstalled);

        await Create().RemindAtStartupAsync();

        Assert.Empty(_dialogs.OptionConfirms);
        Assert.Empty(_dialogs.Errors);
    }

    [Fact]
    public async Task RemindAtStartupAsync_ReminderOff_ReadsNothing()
    {
        await Create(reminder: false).RemindAtStartupAsync();

        Assert.Empty(_hooks.StatusRequests);
        Assert.Empty(_dialogs.OptionConfirms);
    }

    [Fact]
    public async Task RemindAtStartupAsync_CalledAgain_AsksOnce()
    {
        var vm = Create();

        await vm.RemindAtStartupAsync();
        await vm.RemindAtStartupAsync();

        Assert.Single(_dialogs.OptionConfirms);
    }

    [Fact]
    public async Task LanguageChanged_RerendersTheAccountText()
    {
        var vm = await LoadedAsync();

        Localization.SetLanguage(AppLanguage.SimplifiedChinese);

        Assert.Equal("Steam 账户：Survivor", vm.AccountText);
    }
}
