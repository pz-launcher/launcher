using System.Globalization;
using PzLauncher.Core.Installs;
using PzLauncher.Core.Java;
using PzLauncher.Core.Launch;
using PzLauncher.Core.Mod;
using PzLauncher.Core.Mod.ZombieBuddy;
using PzLauncher.Core.Update;
using PzLauncher.Desktop.Localization;
using PzLauncher.Desktop.Tests.TestSupport;

namespace PzLauncher.Desktop.Tests.Localization;

[Collection(UiTextCultureCollection.Name)]
public class DisplayNamesTests : UiTextCultureTest
{
    [Theory]
    [MemberData(nameof(TestLanguages.All), MemberType = typeof(TestLanguages))]
    public void Of_EveryShownEnum_HasDistinctNonEmptyName(AppLanguage language)
    {
        Localization.SetLanguage(language);

        AssertDistinctNonEmpty([.. Enum.GetValues<PzInstallKind>().Select(DisplayNames.Of)]);
        AssertDistinctNonEmpty([.. Enum.GetValues<PzInstallSource>().Select(DisplayNames.Of)]);
        AssertDistinctNonEmpty([.. Enum.GetValues<GarbageCollector>().Select(DisplayNames.Of)]);
        AssertDistinctNonEmpty([.. Enum.GetValues<InstalledModSource>().Select(DisplayNames.Of)]);
        AssertDistinctNonEmpty([.. Enum.GetValues<ZombieBuddyChannel>().Select(DisplayNames.Of)]);
        AssertDistinctNonEmpty([.. Enum.GetValues<ZombieBuddyStep>().Select(DisplayNames.Of)]);
        AssertDistinctNonEmpty([.. Enum.GetValues<UpdateSource>().Select(DisplayNames.Of)]);
    }

    [Theory]
    [MemberData(nameof(TestLanguages.All), MemberType = typeof(TestLanguages))]
    public void Of_JavaRuntimeSource_DiscoveredSourcesShareOneNameDistinctFromManual(AppLanguage language)
    {
        Localization.SetLanguage(language);

        var discovered = Enum.GetValues<JavaRuntimeSource>().Where(source => source != JavaRuntimeSource.Manual).Select(DisplayNames.Of).Distinct().ToList();

        Assert.False(string.IsNullOrWhiteSpace(Assert.Single(discovered)));
        Assert.NotEqual(discovered[0], DisplayNames.Of(JavaRuntimeSource.Manual));
        Assert.False(string.IsNullOrWhiteSpace(DisplayNames.Of(JavaRuntimeSource.Manual)));
    }

    [Fact]
    public void Converters_EnumValue_ReturnDisplayName()
    {
        Assert.Equal(
            DisplayNames.Of(PzInstallKind.DedicatedServer),
            DisplayNames.InstallKind.Convert(PzInstallKind.DedicatedServer, typeof(string), null, CultureInfo.InvariantCulture));
        Assert.Equal(
            DisplayNames.Of(PzInstallSource.Manual),
            DisplayNames.InstallSource.Convert(PzInstallSource.Manual, typeof(string), null, CultureInfo.InvariantCulture));
        Assert.Equal(
            DisplayNames.Of(JavaRuntimeSource.Manual),
            DisplayNames.JavaSource.Convert(JavaRuntimeSource.Manual, typeof(string), null, CultureInfo.InvariantCulture));
        Assert.Equal(
            DisplayNames.Of(InstalledModSource.Local),
            DisplayNames.ModSource.Convert(InstalledModSource.Local, typeof(string), null, CultureInfo.InvariantCulture));
        Assert.Equal(
            DisplayNames.Of(ZombieBuddyChannel.Beta),
            DisplayNames.ZombieBuddyChannelName.Convert(ZombieBuddyChannel.Beta, typeof(string), null, CultureInfo.InvariantCulture));
        Assert.Equal(
            DisplayNames.Of(ZombieBuddyStep.CopyAgentJar),
            DisplayNames.ZombieBuddyStepName.Convert(ZombieBuddyStep.CopyAgentJar, typeof(string), null, CultureInfo.InvariantCulture));
    }

    private static void AssertDistinctNonEmpty(string[] names)
    {
        Assert.All(names, name => Assert.False(string.IsNullOrWhiteSpace(name)));
        Assert.Equal(names.Length, names.Distinct(StringComparer.Ordinal).Count());
    }
}
