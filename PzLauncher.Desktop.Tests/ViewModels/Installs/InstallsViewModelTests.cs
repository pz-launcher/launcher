using PzLauncher.Core.Common;
using PzLauncher.Core.Installs;
using PzLauncher.Desktop.Localization;
using PzLauncher.Desktop.Services;
using PzLauncher.Desktop.Tests.TestSupport;
using PzLauncher.Desktop.ViewModels.Installs;
using static PzLauncher.Desktop.Tests.TestSupport.TestInstalls;

namespace PzLauncher.Desktop.Tests.ViewModels.Installs;

[Collection(UiTextCultureCollection.Name)]
public class InstallsViewModelTests : UiTextCultureTest
{
    private readonly FakeInstallDetectionService _service = new();
    private readonly FakeFolderPicker _picker = new();

    private InstallsViewModel CreateViewModel() => new(_service, _picker, new FakePlatformEnvironment(), Localization);

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
        var manual = new PzInstall(PzInstallKind.Game, @"D:\Games\PZ", Steam: null);
        _service.Inspect = _ => manual;

        await AddFolderAsync(vm, @"D:\Games\PZ");

        Assert.Equal([SteamGame, manual], vm.Installs);
        Assert.Equal([new InstallRow(SteamGame, true), new InstallRow(manual, false)], vm.VisibleInstalls);
        Assert.Equal(SteamGame, vm.SelectedInstall);
        Assert.Null(vm.ErrorText);
        Assert.Equal([@"D:\Games\PZ"], _service.InspectedPaths);
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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ScanAsync_FirstScan_SelectsFirstInstallOrNone(bool found)
    {
        var vm = found ? await CreateScannedViewModelAsync(SteamGame, SteamServer) : await CreateScannedViewModelAsync();

        Assert.Equal(found ? SteamGame : null, vm.SelectedInstall);
    }

    [Fact]
    public async Task ScanAsync_SelectedStillPresent_KeepsItByPathWithFreshData()
    {
        var vm = await CreateScannedViewModelAsync(SteamGame, SteamServer);
        vm.Use(SteamServer.RootPath);
        var updated = SteamServer with { RootPath = SteamServer.RootPath.ToUpperInvariant(), Steam = new SteamAppInfo("201", "unstable") };
        _service.ScanResults.Enqueue(FakeInstallDetectionService.Found(SteamGame, updated));

        await vm.ScanCommand.ExecuteAsync(null);

        Assert.Equal(updated, vm.SelectedInstall);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ScanAsync_SelectedGone_FallsBackToFirstOrNone(bool othersLeft)
    {
        var vm = await CreateScannedViewModelAsync(SteamGame, SteamServer);
        vm.Use(SteamServer.RootPath);
        _service.ScanResults.Enqueue(othersLeft ? FakeInstallDetectionService.Found(SteamGame) : FakeInstallDetectionService.Found());

        await vm.ScanCommand.ExecuteAsync(null);

        Assert.Equal(othersLeft ? SteamGame : null, vm.SelectedInstall);
    }

    [Fact]
    public async Task ScanAsync_Fails_KeepsSelection()
    {
        var vm = await CreateScannedViewModelAsync(SteamGame, SteamServer);
        vm.Use(SteamServer.RootPath);
        _service.ScanResults.Enqueue(Result<InstallScanResult>.Fail(TestErrors.Create(ErrorCode.SteamNotInstalled)));

        await vm.ScanCommand.ExecuteAsync(null);

        Assert.Equal(SteamServer, vm.SelectedInstall);
    }

    [Fact]
    public void Filter_GameAndServerOnly_GameByDefault()
    {
        var vm = CreateViewModel();

        Assert.Equal([InstallsFilter.Game, InstallsFilter.DedicatedServer], Enum.GetValues<InstallsFilter>());
        Assert.Equal(InstallsFilter.Game, vm.Filter);
    }

    [Theory]
    [InlineData(InstallsFilter.Game)]
    [InlineData(InstallsFilter.DedicatedServer)]
    public async Task Filter_Set_ShowsOnlyThatKind(InstallsFilter filter)
    {
        var vm = await CreateScannedViewModelAsync(SteamGame, SteamServer);

        vm.Filter = filter;

        PzInstall[] expected = filter switch
        {
            InstallsFilter.Game => [SteamGame],
            InstallsFilter.DedicatedServer => [SteamServer],
        };
        Assert.Equal(expected, vm.VisibleInstalls.Select(row => row.Install));
        Assert.Equal([SteamGame, SteamServer], vm.Installs);
        Assert.False(vm.IsFilterEmpty);
    }

    [Fact]
    public async Task VisibleInstalls_MarkTheInstallInUse()
    {
        var vm = await CreateScannedViewModelAsync(SteamGame, ManualGame);

        Assert.Equal([new InstallRow(SteamGame, true), new InstallRow(ManualGame, false)], vm.VisibleInstalls);

        vm.Use(ManualGame.RootPath);

        Assert.Equal([new InstallRow(SteamGame, false), new InstallRow(ManualGame, true)], vm.VisibleInstalls);
    }

    [Fact]
    public async Task OpenSettings_RequestsVersionSettingsForTheRowAndKeepsTheInstallInUse()
    {
        var vm = await CreateScannedViewModelAsync(SteamGame, ManualGame);
        var requested = new List<PzInstall>();
        vm.VersionSettingsRequested += (_, install) => requested.Add(install);

        vm.OpenSettingsCommand.Execute(ManualGame);

        Assert.Equal([ManualGame], requested);
        Assert.Equal(SteamGame, vm.SelectedInstall);
    }

    [Theory]
    [InlineData(@"D:\GAMES\PROJECTZOMBOID", true)]
    [InlineData(@"D:\Games\Gone", false)]
    public async Task Use_MatchesTheRootByPlatformComparisonAndIgnoresUnlistedRoots(string root, bool listed)
    {
        var vm = await CreateScannedViewModelAsync(SteamGame, ManualGame);

        vm.Use(root);

        Assert.Equal(listed ? ManualGame : SteamGame, vm.SelectedInstall);
        Assert.Equal(listed, vm.IsListed(root));
        Assert.Equal(listed, vm.IsInUse(root));
    }

    [Fact]
    public async Task Filter_KindWithoutInstalls_IsFilterEmptyNotEmpty()
    {
        var vm = await CreateScannedViewModelAsync(SteamGame);

        vm.Filter = InstallsFilter.DedicatedServer;

        Assert.Empty(vm.VisibleInstalls);
        Assert.True(vm.IsFilterEmpty);
        Assert.False(vm.IsEmpty);
        Assert.Equal(SteamGame, vm.SelectedInstall);
    }

    [Fact]
    public async Task LanguageChanged_ShownErrorAndWarnings_AreReRenderedWithoutRescanning()
    {
        var warning = TestErrors.Create(ErrorCode.SteamLibraryUnreadable, @"C:\Steam\steamapps\libraryfolders.vdf");
        var error = TestErrors.Create(ErrorCode.InstallNotAPzInstall, @"D:\Random");
        _service.ScanResults.Enqueue(new InstallScanResult([SteamGame], [warning]));
        _service.Inspect = _ => error;
        var vm = CreateViewModel();
        await vm.ScanCommand.ExecuteAsync(null);
        await AddFolderAsync(vm, @"D:\Random");
        var englishError = vm.ErrorText;
        var englishWarnings = vm.Warnings.ToList();

        Localization.SetLanguage(AppLanguage.SimplifiedChinese);

        Assert.Equal(ErrorMessages.ToUserText(error), vm.ErrorText);
        Assert.NotEqual(englishError, vm.ErrorText);
        Assert.Equal([ErrorMessages.ToUserText(warning)], vm.Warnings);
        Assert.NotEqual(englishWarnings, vm.Warnings);
        Assert.Equal(1, _service.ScanCalls);
    }

    [Fact]
    public async Task LanguageChanged_FolderNotLocalShown_IsReRendered()
    {
        var vm = CreateViewModel();
        _picker.Next = new FolderPickResult.NotLocal("smb://nas/games/pz");
        await vm.AddFolderCommand.ExecuteAsync(null);

        Localization.SetLanguage(AppLanguage.TraditionalChinese);

        Assert.Equal(ErrorMessages.FolderNotLocal("smb://nas/games/pz"), vm.ErrorText);
    }

    [Fact]
    public void LanguageChanged_NoErrorShown_KeepsErrorTextNull()
    {
        var vm = CreateViewModel();

        Localization.SetLanguage(AppLanguage.TraditionalChinese);

        Assert.Null(vm.ErrorText);
    }
}
