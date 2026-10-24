using PzLauncher.Core.Common;
using PzLauncher.Core.Update.Releases;
using PzLauncher.Core.Update;
using PzLauncher.Desktop.Services.Launch;
using PzLauncher.Desktop.Services.Preferences;
using PzLauncher.Desktop.Services.Update;
using PzLauncher.Desktop.Tests.TestSupport;
using static PzLauncher.Desktop.Tests.TestSupport.Fakes.TestUpdates;

namespace PzLauncher.Desktop.Tests.Services.Update;

public class UpdateCoordinatorTests
{
    private readonly FakeUpdateService _updates = new();
    private readonly FakePreferenceStore _store = new();
    private readonly GameRunState _runState = new();
    private readonly FakeAppLifetime _app = new();

    private UpdateCoordinator Create(UserPreferences? saved = null) => Coordinator(_updates, _store, saved, _runState, _app);

    [Fact]
    public async Task Start_AutoCheckOff_MakesNoRequest()
    {
        var coordinator = Create(new UserPreferences(null, null, null) { AutoCheckUpdates = false });

        await coordinator.StartAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, _updates.RequestCount);
        Assert.Equal(UpdateStatus.Idle, coordinator.Status);
    }

    [Fact]
    public async Task Start_DevelopmentBuild_DoesNothing()
    {
        _updates.CurrentVersion = SemVersion.Development;
        _updates.StagedAtStart = Staged(Version("0.3.0"));
        var coordinator = Create();

        await coordinator.StartAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, _updates.RequestCount);
        Assert.Equal(0, _updates.CleanUpCalls);
        Assert.Equal(0, _updates.GetStagedCalls);
        Assert.Null(coordinator.Staged);
    }

    [Fact]
    public async Task Start_PackageStagedEarlier_IsReadyWithoutNetwork()
    {
        var staged = Staged(Version("0.3.0"));
        _updates.StagedAtStart = staged;
        var coordinator = Create();

        await coordinator.StartAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, _updates.RequestCount);
        Assert.Equal(1, _updates.CleanUpCalls);
        Assert.Equal(UpdateStatus.Ready, coordinator.Status);
        Assert.Same(staged, coordinator.Staged);
    }

    [Fact]
    public async Task Start_NewerRelease_DownloadsWithProgressAndIsReady()
    {
        _updates.Check = _ => Found("0.3.0", UpdateSource.Gitee);
        _updates.DownloadReports = [new DownloadProgress(250, null), new DownloadProgress(500, 1000)];
        var coordinator = Create();
        var percents = new List<int?>();
        coordinator.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(UpdateCoordinator.DownloadPercent))
            {
                percents.Add(coordinator.DownloadPercent);
            }
        };

        await coordinator.StartAsync(TestContext.Current.CancellationToken);

        Assert.Equal([UpdateSource.Auto], _updates.Checks);
        Assert.Equal(Version("0.3.0"), Assert.Single(_updates.Downloads).Version);
        Assert.Equal([25, 50], percents);
        Assert.Equal(UpdateStatus.Ready, coordinator.Status);
        Assert.Equal(Version("0.3.0"), coordinator.Staged?.Version);
    }

    [Fact]
    public async Task Start_NothingNewer_IsUpToDate()
    {
        var coordinator = Create();

        await coordinator.StartAsync(TestContext.Current.CancellationToken);

        Assert.Single(_updates.Checks);
        Assert.Empty(_updates.Downloads);
        Assert.Equal(UpdateStatus.UpToDate, coordinator.Status);
    }

    [Fact]
    public async Task Start_CheckFails_IsOnlyLogged()
    {
        _updates.Check = _ => TestErrors.Create(ErrorCode.UpdateNetworkFailed);
        var coordinator = Create();

        await coordinator.StartAsync(TestContext.Current.CancellationToken);

        Assert.Equal(UpdateStatus.Idle, coordinator.Status);
        Assert.Null(coordinator.Error);
    }

    [Fact]
    public async Task Start_DownloadFails_IsOnlyLogged()
    {
        _updates.Check = _ => Found();
        _updates.Download = _ => TestErrors.Create(ErrorCode.UpdatePackageInvalid);
        var coordinator = Create();

        await coordinator.StartAsync(TestContext.Current.CancellationToken);

        Assert.Equal(UpdateStatus.Idle, coordinator.Status);
        Assert.Null(coordinator.Error);
        Assert.Null(coordinator.Staged);
    }

    [Fact]
    public async Task Start_FolderNotWritable_OffersTheReleaseWithoutDownloading()
    {
        _updates.Check = _ => Found();
        _updates.CanInstallInPlace = false;
        var coordinator = Create();

        await coordinator.StartAsync(TestContext.Current.CancellationToken);

        Assert.Empty(_updates.Downloads);
        Assert.Equal(UpdateStatus.Available, coordinator.Status);
        Assert.Equal(Version("0.3.0"), coordinator.Available?.Version);
        Assert.Null(coordinator.Staged);
    }

    [Fact]
    public async Task Start_CalledTwice_RunsOnce()
    {
        var coordinator = Create();

        await coordinator.StartAsync(TestContext.Current.CancellationToken);
        await coordinator.StartAsync(TestContext.Current.CancellationToken);

        Assert.Single(_updates.Checks);
    }

    [Theory]
    [InlineData(UpdateSource.Auto)]
    [InlineData(UpdateSource.GitHub)]
    [InlineData(UpdateSource.Gitee)]
    public async Task CheckNow_AutoCheckOff_StillChecksTheSavedSource(UpdateSource source)
    {
        var coordinator = Create(new UserPreferences(null, null, null) { UpdateSource = source, AutoCheckUpdates = false });

        await coordinator.CheckNowAsync(TestContext.Current.CancellationToken);

        Assert.Equal([source], _updates.Checks);
        Assert.Equal(UpdateStatus.UpToDate, coordinator.Status);
    }

    [Fact]
    public async Task CheckNow_Fails_ShowsTheError()
    {
        _updates.Check = _ => TestErrors.Create(ErrorCode.UpdateNetworkFailed);
        var coordinator = Create();

        await coordinator.CheckNowAsync(TestContext.Current.CancellationToken);

        Assert.Equal(UpdateStatus.Failed, coordinator.Status);
        Assert.Equal(ErrorCode.UpdateNetworkFailed, coordinator.Error?.Code);
    }

    [Fact]
    public async Task CheckNow_DevelopmentBuild_ShowsDevelopmentBuildError()
    {
        _updates.CurrentVersion = SemVersion.Development;
        var coordinator = Create();

        await coordinator.CheckNowAsync(TestContext.Current.CancellationToken);

        Assert.Equal(UpdateStatus.Failed, coordinator.Status);
        Assert.Equal(ErrorCode.UpdateDevelopmentBuild, coordinator.Error?.Code);
    }

    [Fact]
    public async Task CheckNow_WhileDownloading_IsIgnored()
    {
        _updates.Check = _ => Found();
        _updates.DownloadGate = new TaskCompletionSource();
        var coordinator = Create();
        var first = coordinator.CheckNowAsync(TestContext.Current.CancellationToken);

        Assert.Equal(UpdateStatus.Downloading, coordinator.Status);
        Assert.False(coordinator.CanCheck);
        await coordinator.CheckNowAsync(TestContext.Current.CancellationToken);

        _updates.DownloadGate.SetResult();
        await first;
        Assert.Single(_updates.Checks);
        Assert.Equal(UpdateStatus.Ready, coordinator.Status);
        Assert.True(coordinator.CanCheck);
    }

    [Fact]
    public async Task CheckNow_ReadyAndNothingNewer_StaysReady()
    {
        var staged = Staged(Version("0.3.0"));
        _updates.StagedAtStart = staged;
        _updates.Check = _ => Found("0.3.0");
        var coordinator = Create();
        await coordinator.StartAsync(TestContext.Current.CancellationToken);

        await coordinator.CheckNowAsync(TestContext.Current.CancellationToken);

        Assert.Empty(_updates.Downloads);
        Assert.Equal(UpdateStatus.Ready, coordinator.Status);
        Assert.Same(staged, coordinator.Staged);
    }

    [Fact]
    public async Task CheckNow_FailsWhileReady_KeepsTheStagedPackage()
    {
        _updates.StagedAtStart = Staged(Version("0.3.0"));
        _updates.Check = _ => TestErrors.Create(ErrorCode.UpdateNetworkFailed);
        var coordinator = Create();
        await coordinator.StartAsync(TestContext.Current.CancellationToken);

        await coordinator.CheckNowAsync(TestContext.Current.CancellationToken);

        Assert.Equal(UpdateStatus.Failed, coordinator.Status);
        Assert.NotNull(coordinator.Staged);
    }

    [Fact]
    public void Preferences_SavedChoices_AreAppliedWithoutSaving()
    {
        var coordinator = Create(new UserPreferences(null, null, null) { UpdateSource = UpdateSource.Gitee, AutoCheckUpdates = false });

        Assert.Equal(UpdateSource.Gitee, coordinator.Source);
        Assert.False(coordinator.AutoCheck);
        Assert.Empty(_store.SavedUpdateSources);
        Assert.Empty(_store.SavedAutoCheckUpdates);
    }

    [Fact]
    public void Preferences_MissingChoices_DefaultToAutoAndOn()
    {
        var coordinator = Create();

        Assert.Equal(UpdateSource.Auto, coordinator.Source);
        Assert.True(coordinator.AutoCheck);
    }

    [Fact]
    public void Preferences_Changed_AreSaved()
    {
        var coordinator = Create();

        coordinator.Source = UpdateSource.GitHub;
        coordinator.AutoCheck = false;

        Assert.Equal([UpdateSource.GitHub], _store.SavedUpdateSources);
        Assert.Equal([false], _store.SavedAutoCheckUpdates);
    }

    [Fact]
    public async Task Restart_InstallsStartsTheNewVersionAndCloses()
    {
        var staged = Staged(Version("0.3.0"));
        _updates.StagedAtStart = staged;
        var coordinator = Create();
        await coordinator.StartAsync(TestContext.Current.CancellationToken);

        await coordinator.RestartAsync();

        Assert.Equal([staged], _updates.Installs);
        Assert.Equal(1, _updates.StartCalls);
        Assert.Equal(1, _app.CloseCalls);
        Assert.Null(coordinator.Staged);
    }

    [Fact]
    public async Task Restart_GameRunning_IsBlocked()
    {
        _updates.StagedAtStart = Staged(Version("0.3.0"));
        var coordinator = Create();
        await coordinator.StartAsync(TestContext.Current.CancellationToken);
        var changed = new List<string?>();
        coordinator.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        _runState.IsGameRunning = true;
        await coordinator.RestartAsync();

        Assert.False(coordinator.CanRestart);
        Assert.Contains(nameof(UpdateCoordinator.CanRestart), changed);
        Assert.Empty(_updates.Installs);
        Assert.Equal(0, _app.CloseCalls);

        _runState.IsGameRunning = false;
        Assert.True(coordinator.CanRestart);
    }

    [Fact]
    public async Task Restart_WhileANewerVersionDownloads_IsBlocked()
    {
        _updates.StagedAtStart = Staged(Version("0.3.0"));
        _updates.Check = _ => Found("0.4.0");
        _updates.DownloadGate = new TaskCompletionSource();
        var coordinator = Create();
        await coordinator.StartAsync(TestContext.Current.CancellationToken);
        var download = coordinator.CheckNowAsync(TestContext.Current.CancellationToken);

        Assert.False(coordinator.CanRestart);
        await coordinator.RestartAsync();
        Assert.Empty(_updates.Installs);

        _updates.DownloadGate.SetResult();
        await download;
        Assert.True(coordinator.CanRestart);
    }

    [Fact]
    public async Task Restart_InstallFails_KeepsRunningAndOffersItAgain()
    {
        _updates.StagedAtStart = Staged(Version("0.3.0"));
        _updates.Install = _ => TestErrors.Create(ErrorCode.UpdateInstallFailed, @"C:\Games\PzLauncher");
        var coordinator = Create();
        await coordinator.StartAsync(TestContext.Current.CancellationToken);

        await coordinator.RestartAsync();

        Assert.Equal(ErrorCode.UpdateInstallFailed, coordinator.RestartError?.Code);
        Assert.NotNull(coordinator.Staged);
        Assert.True(coordinator.CanRestart);
        Assert.Equal(0, _updates.StartCalls);
        Assert.Equal(0, _app.CloseCalls);
    }

    [Fact]
    public async Task Restart_NewVersionDoesNotStart_IsInstalledWithTheError()
    {
        _updates.StagedAtStart = Staged(Version("0.3.0"));
        _updates.StartInstalled = () => TestErrors.Create(ErrorCode.UpdateRestartFailed, @"C:\Games\PzLauncher\PzLauncher.exe");
        var coordinator = Create();
        await coordinator.StartAsync(TestContext.Current.CancellationToken);

        await coordinator.RestartAsync();

        Assert.Equal(UpdateStatus.Installed, coordinator.Status);
        Assert.Equal(Version("0.3.0"), coordinator.InstalledVersion);
        Assert.Equal(ErrorCode.UpdateRestartFailed, coordinator.RestartError?.Code);
        Assert.False(coordinator.CanCheck);
        Assert.Equal(0, _app.CloseCalls);
    }

    [Fact]
    public async Task InstallOnClose_Staged_Installs()
    {
        var staged = Staged(Version("0.3.0"));
        _updates.StagedAtStart = staged;
        var coordinator = Create();
        await coordinator.StartAsync(TestContext.Current.CancellationToken);

        await coordinator.InstallPendingOnCloseAsync(TestContext.Current.CancellationToken);

        Assert.Equal([staged], _updates.Installs);
        Assert.Null(coordinator.Staged);
        Assert.Equal(0, _updates.StartCalls);
    }

    [Fact]
    public async Task InstallOnClose_GameRunning_StillInstalls()
    {
        _updates.StagedAtStart = Staged(Version("0.3.0"));
        _runState.IsGameRunning = true;
        var coordinator = Create();
        await coordinator.StartAsync(TestContext.Current.CancellationToken);

        await coordinator.InstallPendingOnCloseAsync(TestContext.Current.CancellationToken);

        Assert.Single(_updates.Installs);
    }

    [Fact]
    public async Task InstallOnClose_NothingStaged_DoesNothing()
    {
        var coordinator = Create();
        await coordinator.StartAsync(TestContext.Current.CancellationToken);

        await coordinator.InstallPendingOnCloseAsync(TestContext.Current.CancellationToken);

        Assert.Empty(_updates.Installs);
    }
}
