using PzLauncher.Core.Common;
using PzLauncher.Core.Update;
using PzLauncher.Desktop.Localization;
using PzLauncher.Desktop.Services.Preferences;
using PzLauncher.Desktop.Services.Update;
using PzLauncher.Desktop.Tests.TestSupport;
using PzLauncher.Desktop.ViewModels.Update;
using static PzLauncher.Desktop.Tests.TestSupport.TestUpdates;

namespace PzLauncher.Desktop.Tests.ViewModels.Update;

[Collection(UiTextCultureCollection.Name)]
public class UpdateSettingsViewModelTests : UiTextCultureTest
{
    private readonly FakeUpdateService _updates = new();
    private readonly FakePreferenceStore _store = new();

    private (UpdateCoordinator Coordinator, UpdateSettingsViewModel Settings) Create(UserPreferences? saved = null)
    {
        var coordinator = Coordinator(_updates, _store, saved);
        return (coordinator, new UpdateSettingsViewModel(coordinator, Localization));
    }

    [Fact]
    public void Constructor_ShowsCurrentVersionAndSavedChoices()
    {
        var (_, settings) = Create(new UserPreferences(null, null, null) { UpdateSource = UpdateSource.Gitee, AutoCheckUpdates = false });

        Assert.Equal("Current version: 0.2.0", settings.CurrentVersionText);
        Assert.Equal(UpdateSource.Gitee, settings.Source);
        Assert.False(settings.AutoCheck);
        Assert.Null(settings.StatusText);
    }

    [Fact]
    public void Choices_Changed_AreSavedAndNotified()
    {
        var (_, settings) = Create();
        var changed = new List<string?>();
        settings.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        settings.Source = UpdateSource.GitHub;
        settings.AutoCheck = false;

        Assert.Equal([UpdateSource.GitHub], _store.SavedUpdateSources);
        Assert.Equal([false], _store.SavedAutoCheckUpdates);
        Assert.Contains(nameof(UpdateSettingsViewModel.Source), changed);
        Assert.Contains(nameof(UpdateSettingsViewModel.AutoCheck), changed);
    }

    [Fact]
    public async Task CheckNow_AutoCheckOff_ChecksAndShowsUpToDate()
    {
        var (_, settings) = Create(new UserPreferences(null, null, null) { AutoCheckUpdates = false });

        await settings.CheckNowCommand.ExecuteAsync(null);

        Assert.Single(_updates.Checks);
        Assert.Equal("PzLauncher is up to date.", settings.StatusText);
        Assert.False(settings.IsStatusError);
    }

    [Fact]
    public async Task CheckNow_DevelopmentBuild_ShowsItsErrorText()
    {
        _updates.CurrentVersion = SemVersion.Development;
        var (_, settings) = Create();

        await settings.CheckNowCommand.ExecuteAsync(null);

        Assert.Equal(ErrorMessages.ToUserText(TestErrors.Create(ErrorCode.UpdateDevelopmentBuild)), settings.StatusText);
        Assert.True(settings.IsStatusError);
    }

    [Fact]
    public async Task CheckNow_Downloading_ShowsVersionSourceAndPercent()
    {
        _updates.Check = _ => Found("0.3.0", UpdateSource.Gitee);
        _updates.DownloadReports = [new DownloadProgress(420, 1000)];
        _updates.DownloadGate = new TaskCompletionSource();
        var (_, settings) = Create();

        var check = settings.CheckNowCommand.ExecuteAsync(null);

        Assert.Equal("Downloading version 0.3.0 from Gitee… 42%", settings.StatusText);
        Assert.True(settings.IsBusy);
        Assert.False(settings.IsProgressIndeterminate);
        Assert.Equal(42, settings.ProgressPercent);
        Assert.False(settings.CheckNowCommand.CanExecute(null));

        _updates.DownloadGate.SetResult();
        await check;

        Assert.Equal("Version 0.3.0 has been downloaded. It installs when you restart or close PzLauncher.", settings.StatusText);
        Assert.False(settings.IsBusy);
        Assert.True(settings.CheckNowCommand.CanExecute(null));
    }

    [Fact]
    public async Task CheckNow_FolderNotWritable_SaysSoInTheStatus()
    {
        _updates.Check = _ => Found("0.3.0");
        _updates.CanInstallInPlace = false;
        var (_, settings) = Create();

        await settings.CheckNowCommand.ExecuteAsync(null);

        Assert.Equal(
            "Version 0.3.0 is available. The launcher can't write to its folder, so download it from the release page.",
            settings.StatusText);
    }

    [Fact]
    public async Task LanguageChanged_RerendersStatusAndVersion()
    {
        var (_, settings) = Create();
        await settings.CheckNowCommand.ExecuteAsync(null);
        var english = (settings.StatusText, settings.CurrentVersionText);

        Localization.SetLanguage(AppLanguage.Russian);

        Assert.NotEqual(english.StatusText, settings.StatusText);
        Assert.NotEqual(english.CurrentVersionText, settings.CurrentVersionText);
    }
}
