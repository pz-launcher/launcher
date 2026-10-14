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

    private async Task<InstallsViewModel> CreateScannedViewModelAsync(params PzInstall[] found)
    {
        _service.ScanResults.Enqueue(FakeInstallDetectionService.Found(found));
        var vm = CreateViewModel();
        await vm.ScanCommand.ExecuteAsync(null);
        return vm;
    }

    private Task AddFolderAsync(InstallsViewModel vm, string path)
    {
        _picker.PickNext(path);
        return vm.AddFolderCommand.ExecuteAsync(null);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ScanAsync_Completes_GoesBusyThenLoadedOrEmpty(bool found)
    {
        PzInstall[] installs = found ? [SteamGame, SteamServer] : [];
        _service.ScanGate = new TaskCompletionSource();
        _service.ScanResults.Enqueue(FakeInstallDetectionService.Found(installs));
        var vm = CreateViewModel();
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        Assert.False(vm.IsEmpty);

        var scan = vm.ScanCommand.ExecuteAsync(null);
        Assert.True(vm.IsScanning);
        Assert.False(vm.IsEmpty);

        _service.ScanGate.SetResult();
        await scan;

        Assert.False(vm.IsScanning);
        Assert.True(vm.HasScanned);
        Assert.Equal(!found, vm.IsEmpty);
        Assert.Contains(nameof(InstallsViewModel.IsEmpty), changed);
        Assert.Equal(installs, vm.Installs);
        Assert.Empty(vm.Warnings);
        Assert.Null(vm.ErrorText);
    }

    [Fact]
    public async Task ScanAsync_Warnings_ShownAsUserTextFromErrorCodeAndReplacedOnRescan()
    {
        var warning = TestErrors.Create(ErrorCode.SteamLibraryUnreadable, @"C:\Steam\steamapps\libraryfolders.vdf");
        _service.ScanResults.Enqueue(new InstallScanResult([SteamGame], [warning]));
        _service.ScanResults.Enqueue(FakeInstallDetectionService.Found(SteamGame));
        var vm = CreateViewModel();

        await vm.ScanCommand.ExecuteAsync(null);

        Assert.Equal([ErrorMessages.ToUserText(warning)], vm.Warnings);
        Assert.Equal([SteamGame], vm.Installs);

        await vm.ScanCommand.ExecuteAsync(null);

        Assert.Empty(vm.Warnings);
    }

    [Fact]
    public async Task ScanAsync_ServiceFails_SetsErrorText()
    {
        var error = TestErrors.Create(ErrorCode.SteamNotInstalled);
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
    public async Task ScanAsync_Rescan_ReplacesSteamEntriesAndKeepsManualOnesSteamDoesNotList()
    {
        var vm = await CreateScannedViewModelAsync(SteamGame);
        await AddFolderAsync(vm, @"D:\Manual\PZ");
        await AddFolderAsync(vm, SteamServer.RootPath);
        var manual = vm.Installs[1];
        _service.ScanResults.Enqueue(FakeInstallDetectionService.Found(SteamServer));

        await vm.ScanCommand.ExecuteAsync(null);

        Assert.Equal([SteamServer, manual], vm.Installs);
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
        var vm = await CreateScannedViewModelAsync(SteamGame);
        var manual = new PzInstall(PzInstallKind.DedicatedServer, @"D:\Servers\PZ", Steam: null);
        _service.Inspect = _ => manual;

        await AddFolderAsync(vm, @"D:\Servers\PZ");

        Assert.Equal([SteamGame, manual], vm.Installs);
        Assert.Null(vm.ErrorText);
        Assert.Equal([@"D:\Servers\PZ"], _service.InspectedPaths);
    }

    [Fact]
    public async Task AddFolderAsync_InspectFailsThenSucceeds_ShowsThenClearsErrorText()
    {
        var error = TestErrors.Create(ErrorCode.InstallNotAPzInstall, @"D:\Random");
        _service.Inspect = _ => error;
        var vm = CreateViewModel();

        await AddFolderAsync(vm, @"D:\Random");

        Assert.Equal(ErrorMessages.ToUserText(error), vm.ErrorText);
        Assert.Empty(vm.Installs);

        _service.Inspect = path => new PzInstall(PzInstallKind.Game, path, Steam: null);
        await AddFolderAsync(vm, @"D:\Game");

        Assert.Null(vm.ErrorText);
        Assert.Single(vm.Installs);
    }

    [Fact]
    public async Task AddFolderAsync_PickerCancelled_ChangesNothing()
    {
        var vm = await CreateScannedViewModelAsync(SteamGame);
        _service.Inspect = _ => TestErrors.Create(ErrorCode.InstallInvalidPath);
        await AddFolderAsync(vm, "bad");
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
        var vm = await CreateScannedViewModelAsync(SteamGame);
        _picker.Next = new FolderPickResult.NotLocal("smb://nas/games/pz");

        await vm.AddFolderCommand.ExecuteAsync(null);

        Assert.Equal(ErrorMessages.FolderNotLocal("smb://nas/games/pz"), vm.ErrorText);
        Assert.Equal([SteamGame], vm.Installs);
        Assert.Empty(_service.InspectedPaths);
    }

    [Fact]
    public async Task AddFolderAsync_PathAlreadyListed_DoesNotDuplicate()
    {
        var vm = await CreateScannedViewModelAsync(SteamGame);

        await AddFolderAsync(vm, SteamGame.RootPath.ToUpperInvariant());

        Assert.Equal([SteamGame], vm.Installs);
    }
}
