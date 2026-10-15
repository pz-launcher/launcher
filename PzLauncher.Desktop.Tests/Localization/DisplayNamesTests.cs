using System.Globalization;
using PzLauncher.Core.Installs;
using PzLauncher.Desktop.Localization;
using PzLauncher.Desktop.Tests.TestSupport;

namespace PzLauncher.Desktop.Tests.Localization;

[Collection(UiTextCultureCollection.Name)]
public class DisplayNamesTests : UiTextCultureTest
{
    [Theory]
    [MemberData(nameof(TestLanguages.All), MemberType = typeof(TestLanguages))]
    public void Of_EveryInstallKindAndSource_HasDistinctNonEmptyName(AppLanguage language)
    {
        Localization.SetLanguage(language);

        AssertDistinctNonEmpty([.. Enum.GetValues<PzInstallKind>().Select(DisplayNames.Of)]);
        AssertDistinctNonEmpty([.. Enum.GetValues<PzInstallSource>().Select(DisplayNames.Of)]);
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
    }

    private static void AssertDistinctNonEmpty(string[] names)
    {
        Assert.All(names, name => Assert.False(string.IsNullOrWhiteSpace(name)));
        Assert.Equal(names.Length, names.Distinct(StringComparer.Ordinal).Count());
    }
}
