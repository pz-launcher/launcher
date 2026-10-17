using PzLauncher.Core.Common;
using PzLauncher.Desktop.Localization;
using PzLauncher.Desktop.Services;
using PzLauncher.Desktop.Tests.TestSupport;
using PzLauncher.Desktop.ViewModels.Update;
using static PzLauncher.Desktop.Tests.TestSupport.TestUpdates;

namespace PzLauncher.Desktop.Tests.ViewModels.Update;

[Collection(UiTextCultureCollection.Name)]
public class UpdateBannerViewModelTests : UiTextCultureTest
{
    private readonly FakeUpdateService _updates = new();
    private readonly GameRunState _runState = new();
    private readonly FakeUrlLauncher _urls = new();
    private readonly FakeAppLifetime _app = new();

    private (UpdateCoordinator Coordinator, UpdateBannerViewModel Banner) Create()
    {
        var coordinator = Coordinator(_updates, runState: _runState, app: _app);
        return (coordinator, new UpdateBannerViewModel(coordinator, _runState, _urls, Localization));
    }

    [Fact]
    public async Task NoUpdate_IsHidden()
    {
        var (coordinator, banner) = Create();

        await coordinator.StartAsync(TestContext.Current.CancellationToken);

        Assert.False(banner.IsVisible);
        Assert.Null(banner.Text);
    }

    [Fact]
    public async Task AfterDownload_OffersRestartAndLater()
    {
        _updates.Check = _ => Found("0.3.0");
        var (coordinator, banner) = Create();
        Assert.False(banner.IsVisible);

        await coordinator.StartAsync(TestContext.Current.CancellationToken);

        Assert.True(banner.IsVisible);
        Assert.Equal("Version 0.3.0 has been downloaded.", banner.Text);
        Assert.True(banner.OffersRestart);
        Assert.False(banner.OffersDownloadPage);
        Assert.False(banner.OffersDismissOnly);
        Assert.True(banner.RestartCommand.CanExecute(null));
        Assert.Null(banner.RestartBlockedText);
    }

    [Fact]
    public async Task GameRunning_DisablesRestartAndSaysWhy()
    {
        _updates.StagedAtStart = Staged(Version("0.3.0"));
        var (coordinator, banner) = Create();
        await coordinator.StartAsync(TestContext.Current.CancellationToken);
        var canExecuteChanges = 0;
        banner.RestartCommand.CanExecuteChanged += (_, _) => canExecuteChanges++;

        _runState.IsGameRunning = true;

        Assert.False(banner.RestartCommand.CanExecute(null));
        Assert.NotNull(banner.RestartBlockedText);
        Assert.True(banner.LaterCommand.CanExecute(null));
        Assert.True(canExecuteChanges > 0);

        _runState.IsGameRunning = false;

        Assert.True(banner.RestartCommand.CanExecute(null));
        Assert.Null(banner.RestartBlockedText);
    }

    [Fact]
    public async Task Later_HidesTheBannerButStillInstallsOnClose()
    {
        _updates.StagedAtStart = Staged(Version("0.3.0"));
        var (coordinator, banner) = Create();
        await coordinator.StartAsync(TestContext.Current.CancellationToken);

        banner.LaterCommand.Execute(null);

        Assert.False(banner.IsVisible);
        await coordinator.InstallPendingOnCloseAsync(TestContext.Current.CancellationToken);
        Assert.Single(_updates.Installs);
    }

    [Fact]
    public async Task Later_ThenANewerVersionIsDownloaded_ShowsAgain()
    {
        _updates.StagedAtStart = Staged(Version("0.3.0"));
        _updates.Check = _ => Found("0.4.0");
        var (coordinator, banner) = Create();
        await coordinator.StartAsync(TestContext.Current.CancellationToken);
        banner.LaterCommand.Execute(null);

        await coordinator.CheckNowAsync(TestContext.Current.CancellationToken);

        Assert.True(banner.IsVisible);
        Assert.Equal("Version 0.4.0 has been downloaded.", banner.Text);
    }

    [Fact]
    public async Task Restart_Executed_InstallsAndCloses()
    {
        _updates.StagedAtStart = Staged(Version("0.3.0"));
        var (coordinator, banner) = Create();
        await coordinator.StartAsync(TestContext.Current.CancellationToken);

        await banner.RestartCommand.ExecuteAsync(null);

        Assert.Single(_updates.Installs);
        Assert.Equal(1, _app.CloseCalls);
    }

    [Fact]
    public async Task Restart_InstallFails_ShowsTheErrorAndKeepsTheOffer()
    {
        _updates.StagedAtStart = Staged(Version("0.3.0"));
        var error = TestErrors.Create(ErrorCode.UpdateInstallFailed, @"C:\Games\PzLauncher");
        _updates.Install = _ => error;
        var (coordinator, banner) = Create();
        await coordinator.StartAsync(TestContext.Current.CancellationToken);

        await banner.RestartCommand.ExecuteAsync(null);

        Assert.True(banner.IsVisible);
        Assert.True(banner.OffersRestart);
        Assert.Equal(ErrorMessages.ToUserText(error), banner.ErrorText);
    }

    [Fact]
    public async Task Restart_NewVersionDoesNotStart_ShowsInstalledWithDismissOnly()
    {
        _updates.StagedAtStart = Staged(Version("0.3.0"));
        _updates.StartInstalled = () => TestErrors.Create(ErrorCode.UpdateRestartFailed, @"C:\Games\PzLauncher\PzLauncher.exe");
        var (coordinator, banner) = Create();
        await coordinator.StartAsync(TestContext.Current.CancellationToken);

        await banner.RestartCommand.ExecuteAsync(null);

        Assert.True(banner.IsVisible);
        Assert.True(banner.OffersDismissOnly);
        Assert.False(banner.OffersRestart);
        Assert.Equal("Version 0.3.0 has been installed. Start PzLauncher again to use it.", banner.Text);
        Assert.NotNull(banner.ErrorText);

        banner.LaterCommand.Execute(null);

        Assert.False(banner.IsVisible);
    }

    [Fact]
    public async Task FolderNotWritable_OffersTheDownloadPage()
    {
        _updates.Check = _ => Found("0.3.0", Core.Update.UpdateSource.Gitee);
        _updates.CanInstallInPlace = false;
        var (coordinator, banner) = Create();
        await coordinator.StartAsync(TestContext.Current.CancellationToken);

        Assert.True(banner.IsVisible);
        Assert.Equal("Version 0.3.0 is available.", banner.Text);
        Assert.True(banner.OffersDownloadPage);
        Assert.False(banner.OffersRestart);

        await banner.OpenDownloadPageCommand.ExecuteAsync(null);

        Assert.Equal([new Uri("https://example.test/releases/v0.3.0")], _urls.Opened);
    }

    [Fact]
    public async Task LanguageChanged_RerendersTheText()
    {
        _updates.StagedAtStart = Staged(Version("0.3.0"));
        var (coordinator, banner) = Create();
        await coordinator.StartAsync(TestContext.Current.CancellationToken);
        var english = banner.Text;

        Localization.SetLanguage(AppLanguage.SimplifiedChinese);

        Assert.NotEqual(english, banner.Text);
        Assert.Contains("0.3.0", banner.Text, StringComparison.Ordinal);
    }
}
