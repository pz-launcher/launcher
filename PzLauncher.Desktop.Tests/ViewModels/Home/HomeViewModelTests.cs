using PzLauncher.Core.Common;
using PzLauncher.Core.Installs;
using PzLauncher.Core.Launch;
using PzLauncher.Desktop.Localization;
using PzLauncher.Desktop.Tests.TestSupport;
using PzLauncher.Desktop.ViewModels.Home;
using PzLauncher.Desktop.ViewModels.Installs;
using static PzLauncher.Desktop.Tests.TestSupport.TestInstalls;

namespace PzLauncher.Desktop.Tests.ViewModels.Home;

[Collection(UiTextCultureCollection.Name)]
public class HomeViewModelTests : UiTextCultureTest
{
    private readonly FakeInstallDetectionService _service = new();
    private readonly FakeGameLaunchService _launcher = new();
    private readonly FakeInstallOverridesService _overrides = new();
    private readonly FakeDialogService _dialogs = new();

    public HomeViewModelTests()
    {
        Installs = new(_service, new FakeFolderPicker(), new FakePlatformEnvironment(), Localization);
    }

    private InstallsViewModel Installs { get; }

    private HomeViewModel CreateViewModel() => new(Installs, _launcher, _overrides, _dialogs);

    private async Task<HomeViewModel> CreateActivatedViewModelAsync(params PzInstall[] installs)
    {
        _service.ScanResults.Enqueue(FakeInstallDetectionService.Found(installs));
        var vm = CreateViewModel();
        await vm.ActivateCommand.ExecuteAsync(null);
        return vm;
    }

    [Fact]
    public async Task SelectedInstall_FollowsTheInstallsPageSelection()
    {
        _service.ScanResults.Enqueue(FakeInstallDetectionService.Found(SteamGame, SteamServer));
        var vm = CreateViewModel();
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        await vm.ActivateCommand.ExecuteAsync(null);

        Assert.Equal(SteamGame, vm.SelectedInstall);
        Assert.False(vm.IsEmpty);

        changed.Clear();
        Installs.ListSelection = SteamServer;

        Assert.Equal(SteamServer, vm.SelectedInstall);
        Assert.Contains(nameof(HomeViewModel.SelectedInstall), changed);
    }

    [Fact]
    public async Task Activate_Scanning_IsBusyThenEmptyWhenNothingFound()
    {
        _service.ScanGate = new TaskCompletionSource();
        var vm = CreateViewModel();

        var activate = vm.ActivateCommand.ExecuteAsync(null);

        Assert.True(vm.IsScanning);
        Assert.False(vm.IsEmpty);

        _service.ScanGate.SetResult();
        await activate;

        Assert.False(vm.IsScanning);
        Assert.True(vm.IsEmpty);
        Assert.Null(vm.SelectedInstall);
    }

    [Fact]
    public async Task Activate_AlsoFromInstallsPage_ScansOnce()
    {
        var vm = CreateViewModel();

        await vm.ActivateCommand.ExecuteAsync(null);
        await Installs.ActivateCommand.ExecuteAsync(null);
        await vm.ActivateCommand.ExecuteAsync(null);

        Assert.Equal(1, _service.ScanCalls);
    }

    [Fact]
    public async Task LaunchAndVersionSettings_EnabledOnlyForAGameInstall()
    {
        var vm = CreateViewModel();
        Assert.False(vm.LaunchCommand.CanExecute(null));
        Assert.False(vm.VersionSettingsCommand.CanExecute(null));

        _service.ScanResults.Enqueue(FakeInstallDetectionService.Found(SteamServer, SteamGame));
        var raised = 0;
        vm.LaunchCommand.CanExecuteChanged += (_, _) => raised++;
        await vm.ActivateCommand.ExecuteAsync(null);

        Assert.Equal(SteamServer, vm.SelectedInstall);
        Assert.False(vm.LaunchCommand.CanExecute(null));
        Assert.False(vm.VersionSettingsCommand.CanExecute(null));

        Installs.ListSelection = SteamGame;

        Assert.True(vm.LaunchCommand.CanExecute(null));
        Assert.True(vm.VersionSettingsCommand.CanExecute(null));
        Assert.True(raised > 0);
    }

    [Fact]
    public async Task Launch_Succeeds_RunningForEveryGameUntilExit()
    {
        var vm = await CreateActivatedViewModelAsync(SteamGame, ManualGame);

        var launch = vm.LaunchCommand.ExecuteAsync(null);

        Assert.True(vm.IsGameRunning);
        Assert.False(vm.LaunchCommand.CanExecute(null));
        Installs.ListSelection = ManualGame;
        Assert.False(vm.LaunchCommand.CanExecute(null));
        Assert.True(vm.VersionSettingsCommand.CanExecute(null));

        _launcher.Exit.SetResult();
        await launch;

        Assert.False(vm.IsGameRunning);
        Assert.True(vm.LaunchCommand.CanExecute(null));
        Assert.Empty(_dialogs.Errors);
    }

    [Fact]
    public async Task Launch_PassesTheSelectedInstallsStoredOverrides()
    {
        var stored = new GameLaunchOverrides(@"C:\Java\jdk-25", 8192, 4096, "-XX:+UseZGC", "-debug");
        _overrides.Set(ManualGame.RootPath, stored);
        _overrides.Set(SteamGame.RootPath, GameLaunchOverrides.Empty with { ExtraGameArgs = "-other" });
        var vm = await CreateActivatedViewModelAsync(SteamGame, ManualGame);
        Installs.ListSelection = ManualGame;

        var launch = vm.LaunchCommand.ExecuteAsync(null);
        _launcher.Exit.SetResult();
        await launch;

        Assert.Equal([new LaunchGameRequest(ManualGame.RootPath, stored)], _launcher.Launches);
    }

    [Fact]
    public async Task Launch_Fails_ShowsTheErrorsUserTextAndStaysEnabled()
    {
        var error = TestErrors.Create(ErrorCode.LaunchJavaMissing, @"C:\Java\jdk-25\bin\javaw.exe");
        _launcher.Launch = _ => error;
        var vm = await CreateActivatedViewModelAsync(SteamGame);

        await vm.LaunchCommand.ExecuteAsync(null);

        Assert.Equal([ErrorMessages.ToUserText(error)], _dialogs.Errors);
        Assert.False(vm.IsGameRunning);
        Assert.True(vm.LaunchCommand.CanExecute(null));
    }

    [Fact]
    public void SelectVersion_RaisesRequest()
    {
        var vm = CreateViewModel();
        var raised = 0;
        vm.SelectVersionRequested += (_, _) => raised++;

        vm.SelectVersionCommand.Execute(null);

        Assert.Equal(1, raised);
    }

    [Fact]
    public async Task VersionSettings_RaisesRequest()
    {
        var vm = await CreateActivatedViewModelAsync(SteamGame);
        var raised = 0;
        vm.VersionSettingsRequested += (_, _) => raised++;

        vm.VersionSettingsCommand.Execute(null);

        Assert.Equal(1, raised);
    }
}
