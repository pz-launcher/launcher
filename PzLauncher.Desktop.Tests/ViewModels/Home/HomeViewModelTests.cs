using PzLauncher.Desktop.Tests.TestSupport;
using PzLauncher.Desktop.ViewModels.Home;
using PzLauncher.Desktop.ViewModels.Installs;
using static PzLauncher.Desktop.Tests.TestSupport.TestInstalls;

namespace PzLauncher.Desktop.Tests.ViewModels.Home;

[Collection(UiTextCultureCollection.Name)]
public class HomeViewModelTests : UiTextCultureTest
{
    private readonly FakeInstallDetectionService _service = new();

    public HomeViewModelTests()
    {
        Installs = new(_service, new FakeFolderPicker(), new FakePlatformEnvironment(), Localization);
    }

    private InstallsViewModel Installs { get; }

    private HomeViewModel CreateViewModel() => new(Installs);

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
    public void LaunchCommand_CannotExecute() => Assert.False(CreateViewModel().LaunchCommand.CanExecute(null));

    [Fact]
    public void SelectVersion_RaisesRequest()
    {
        var vm = CreateViewModel();
        var raised = 0;
        vm.SelectVersionRequested += (_, _) => raised++;

        vm.SelectVersionCommand.Execute(null);

        Assert.Equal(1, raised);
    }
}
