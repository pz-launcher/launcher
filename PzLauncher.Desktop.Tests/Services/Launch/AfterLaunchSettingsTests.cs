using PzLauncher.Core.Platform;
using PzLauncher.Desktop.Localization;
using PzLauncher.Desktop.Services.Launch;
using PzLauncher.Desktop.Services.Preferences;
using PzLauncher.Desktop.Tests.TestSupport;

namespace PzLauncher.Desktop.Tests.Services.Launch;

[Collection(UiTextCultureCollection.Name)]
public class AfterLaunchSettingsTests : UiTextCultureTest
{
    private readonly FakePreferenceStore _preferences = new();

    private AfterLaunchSettings Create(AfterLaunchAction? saved = null, PlatformKind os = PlatformKind.Windows) =>
        new(_preferences, new UserPreferences(null, null, null) { AfterLaunch = saved }, new FakePlatformEnvironment { Os = os });

    [Fact]
    public void Action_Changed_SavesEachChangeButNotTheLoadedValue()
    {
        var settings = Create(AfterLaunchAction.Minimize);

        Assert.Equal(AfterLaunchAction.Minimize, settings.Action);
        Assert.Empty(_preferences.SavedAfterLaunch);

        settings.Action = AfterLaunchAction.Close;

        Assert.Equal([AfterLaunchAction.Close], _preferences.SavedAfterLaunch);
    }

    [Theory]
    [InlineData(PlatformKind.Windows, true)]
    [InlineData(PlatformKind.Linux, false)]
    public void Options_ByPlatform_OfferTheTrayOnlyWhereItShows(PlatformKind os, bool offersTray)
    {
        var settings = Create(os: os);

        AfterLaunchAction[] expected = offersTray
            ? [AfterLaunchAction.Keep, AfterLaunchAction.Minimize, AfterLaunchAction.MinimizeToTray, AfterLaunchAction.Close]
            : [AfterLaunchAction.Keep, AfterLaunchAction.Minimize, AfterLaunchAction.Close];
        Assert.Equal(offersTray, settings.OffersTray);
        Assert.Equal(expected, settings.Options().Select(option => option.Value));
        Assert.Equal(expected.Select(DisplayNames.Of), settings.Options().Select(option => option.Text));
    }
}
