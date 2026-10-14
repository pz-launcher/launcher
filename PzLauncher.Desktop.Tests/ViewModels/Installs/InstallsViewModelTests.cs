using PzLauncher.Core.Common;
using PzLauncher.Core.Installs;
using PzLauncher.Desktop.Localization;
using PzLauncher.Desktop.Services;
using PzLauncher.Desktop.Tests.TestSupport;
using PzLauncher.Desktop.ViewModels.Installs;

namespace PzLauncher.Desktop.Tests.ViewModels.Installs;

public class InstallsViewModelTests
{
    private static readonly PzInstall SteamGame =
        new(PzInstallKind.Game, @"E:\SteamLibrary\steamapps\common\ProjectZomboid", new SteamAppInfo("100", null));

    private static readonly PzInstall SteamServer =
        new(PzInstallKind.DedicatedServer, @"E:\SteamLibrary\steamapps\common\Project Zomboid Dedicated Server", new SteamAppInfo("200", "unstable"));

    private readonly FakeInstallDetectionService _service = new();
    private readonly FakeFolderPicker _picker = new();

    private InstallsViewModel CreateViewModel() => new(_service, _picker, new FakePlatformEnvironment());

    [Fact]
    public async Task ScanAsync_InstallsFound_GoesBusyThenLoaded()
    {
        _service.ScanGate = new TaskCompletionSource();
        _service.ScanResults.Enqueue(FakeInstallDetectionService.Found(SteamGame, SteamServer));
        var vm = CreateViewModel();

        var scan = vm.ScanCommand.ExecuteAsync(null);

        Assert.True(vm.IsScanning);
        Assert.False(vm.IsEmpty);

        _service.ScanGate.SetResult();
        await scan;

        Assert.False(vm.IsScanning);
        Assert.False(vm.IsEmpty);
        Assert.True(vm.HasScanned);
        Assert.Equal([SteamGame, SteamServer], vm.Installs);
        Assert.Empty(vm.Warnings);
        Assert.Null(vm.ErrorText);
    }

    [Fact]
    public async Task ScanAsync_NothingFound_GoesBusyThenEmpty()
    {
        _service.ScanGate = new TaskCompletionSource();
        var vm = CreateViewModel();
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        var scan = vm.ScanCommand.ExecuteAsync(null);
        Assert.True(vm.IsScanning);
        Assert.False(vm.IsEmpty);

        _service.ScanGate.SetResult();
        await scan;

        Assert.False(vm.IsScanning);
        Assert.True(vm.IsEmpty);
        Assert.Empty(vm.Installs);
        Assert.Contains(nameof(InstallsViewModel.IsEmpty), changed);
    }

    [Fact]
    public void IsEmpty_BeforeFirstScan_IsFalse()
    {
        Assert.False(CreateViewModel().IsEmpty);
    }

    [Fact]
    public async Task ScanAsync_Warnings_ShownAsUserTextFromErrorCode()
    {
        var warning = new Error(ErrorCode.SteamLibraryUnreadable, "developer text", ErrorKind.External)
        {
            Details = new Dictionary<string, string> { ["path"] = @"C:\Steam\steamapps\libraryfolders.vdf" },
        };
        _service.ScanResults.Enqueue(new InstallScanResult([SteamGame], [warning]));
        var vm = CreateViewModel();

        await vm.ScanCommand.ExecuteAsync(null);

        var shown = Assert.Single(vm.Warnings);
        Assert.Equal(ErrorMessages.ToUserText(warning), shown);
        Assert.Single(vm.Installs);
    }

    [Fact]
    public async Task ScanAsync_RescanAfterWarnings_ReplacesWarnings()
    {
        _service.ScanResults.Enqueue(new InstallScanResult([], [new Error(ErrorCode.SteamNotInstalled, "x", ErrorKind.NotFound)]));
        _service.ScanResults.Enqueue(FakeInstallDetectionService.Found(SteamGame));
        var vm = CreateViewModel();

        await vm.ScanCommand.ExecuteAsync(null);
        await vm.ScanCommand.ExecuteAsync(null);

        Assert.Empty(vm.Warnings);
        Assert.Equal([SteamGame], vm.Installs);
    }

    [Fact]
    public async Task ScanAsync_ServiceFails_SetsErrorText()
    {
        var error = new Error(ErrorCode.SteamNotInstalled, "developer text", ErrorKind.NotFound);
        _service.ScanResults.Enqueue(Result<InstallScanResult>.Fail(error));
        var vm = CreateViewModel();

        await vm.ScanCommand.ExecuteAsync(null);

        Assert.Equal(ErrorMessages.ToUserText(error), vm.ErrorText);
        Assert.False(vm.IsScanning);
    }

    [Fact]
    public async Task ScanAsync_Cancelled_ReturnsToIdleWithoutError()
    {
        _service.ScanGate = new TaskCompletionSource();
        var vm = CreateViewModel();

        var scan = vm.ScanCommand.ExecuteAsync(null);
        vm.ScanCommand.Cancel();
        await scan;

        Assert.False(vm.IsScanning);
        Assert.Null(vm.ErrorText);
        Assert.Empty(vm.Installs);
    }

    [Fact]
    public async Task ActivateAsync_CalledTwice_ScansOnce()
    {
        var vm = CreateViewModel();

        await vm.ActivateCommand.ExecuteAsync(null);
        await vm.ActivateCommand.ExecuteAsync(null);

        Assert.Equal(1, _service.ScanCalls);
        Assert.True(vm.HasScanned);
    }

    [Fact]
    public async Task AddFolderAsync_ValidFolder_AddsManualInstall()
    {
        _service.ScanResults.Enqueue(FakeInstallDetectionService.Found(SteamGame));
        var vm = CreateViewModel();
        await vm.ScanCommand.ExecuteAsync(null);
        var manual = new PzInstall(PzInstallKind.DedicatedServer, @"D:\Servers\PZ", Steam: null);
        _service.Inspect = _ => manual;
        _picker.PickNext(@"D:\Servers\PZ");

        await vm.AddFolderCommand.ExecuteAsync(null);

        Assert.Equal([SteamGame, manual], vm.Installs);
        Assert.Null(vm.ErrorText);
        Assert.Equal([@"D:\Servers\PZ"], _service.InspectedPaths);
    }

    [Fact]
    public async Task AddFolderAsync_InspectFails_SetsErrorTextFromCode()
    {
        var error = new Error(ErrorCode.InstallNotAPzInstall, "developer text", ErrorKind.Validation)
        {
            Details = new Dictionary<string, string> { ["path"] = @"D:\Random" },
        };
        _service.Inspect = _ => error;
        _picker.PickNext(@"D:\Random");
        var vm = CreateViewModel();

        await vm.AddFolderCommand.ExecuteAsync(null);

        Assert.Equal(ErrorMessages.ToUserText(error), vm.ErrorText);
        Assert.Empty(vm.Installs);
    }

    [Fact]
    public async Task AddFolderAsync_SuccessAfterError_ClearsErrorText()
    {
        _service.Inspect = _ => new Error(ErrorCode.InstallPathNotFound, "x", ErrorKind.NotFound);
        _picker.PickNext(@"D:\Missing");
        var vm = CreateViewModel();
        await vm.AddFolderCommand.ExecuteAsync(null);

        _service.Inspect = path => new PzInstall(PzInstallKind.Game, path, Steam: null);
        _picker.PickNext(@"D:\Game");
        await vm.AddFolderCommand.ExecuteAsync(null);

        Assert.Null(vm.ErrorText);
        Assert.Single(vm.Installs);
    }

    [Fact]
    public async Task AddFolderAsync_PickerCancelled_ChangesNothing()
    {
        _service.ScanResults.Enqueue(FakeInstallDetectionService.Found(SteamGame));
        _service.Inspect = _ => new Error(ErrorCode.InstallInvalidPath, "x", ErrorKind.Validation);
        _picker.PickNext("bad");
        var vm = CreateViewModel();
        await vm.ScanCommand.ExecuteAsync(null);
        await vm.AddFolderCommand.ExecuteAsync(null);
        var errorBefore = vm.ErrorText;
        _service.InspectedPaths.Clear();

        _picker.Next = new FolderPickResult.Cancelled();
        await vm.AddFolderCommand.ExecuteAsync(null);

        Assert.Equal([SteamGame], vm.Installs);
        Assert.Equal(errorBefore, vm.ErrorText);
        Assert.Empty(_service.InspectedPaths);
    }

    [Fact]
    public async Task AddFolderAsync_PickedFolderNotLocal_SetsErrorTextWithoutInspecting()
    {
        _service.ScanResults.Enqueue(FakeInstallDetectionService.Found(SteamGame));
        var vm = CreateViewModel();
        await vm.ScanCommand.ExecuteAsync(null);
        _picker.Next = new FolderPickResult.NotLocal("smb://nas/games/pz");

        await vm.AddFolderCommand.ExecuteAsync(null);

        Assert.Equal(ErrorMessages.FolderNotLocal("smb://nas/games/pz"), vm.ErrorText);
        Assert.Equal([SteamGame], vm.Installs);
        Assert.Empty(_service.InspectedPaths);
    }

    [Fact]
    public async Task AddFolderAsync_PathAlreadyListed_DoesNotDuplicate()
    {
        _service.ScanResults.Enqueue(FakeInstallDetectionService.Found(SteamGame));
        var vm = CreateViewModel();
        await vm.ScanCommand.ExecuteAsync(null);
        _picker.PickNext(SteamGame.RootPath.ToUpperInvariant());

        await vm.AddFolderCommand.ExecuteAsync(null);

        Assert.Equal([SteamGame], vm.Installs);
    }

    [Fact]
    public async Task ScanAsync_Rescan_KeepsManualEntriesAndReplacesSteamEntries()
    {
        _service.ScanResults.Enqueue(FakeInstallDetectionService.Found(SteamGame));
        _service.ScanResults.Enqueue(FakeInstallDetectionService.Found(SteamServer));
        var vm = CreateViewModel();
        await vm.ScanCommand.ExecuteAsync(null);
        _picker.PickNext(@"D:\Manual\PZ");
        await vm.AddFolderCommand.ExecuteAsync(null);
        var manual = vm.Installs[^1];

        await vm.ScanCommand.ExecuteAsync(null);

        Assert.Equal([SteamServer, manual], vm.Installs);
    }

    [Fact]
    public async Task ScanAsync_ManualPathLaterFoundBySteam_ShownOnce()
    {
        var vm = CreateViewModel();
        _picker.PickNext(SteamGame.RootPath);
        await vm.AddFolderCommand.ExecuteAsync(null);
        _service.ScanResults.Enqueue(FakeInstallDetectionService.Found(SteamGame));

        await vm.ScanCommand.ExecuteAsync(null);

        Assert.Equal([SteamGame], vm.Installs);
    }
}
