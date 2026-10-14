using PzLauncher.Desktop.Localization;
using PzLauncher.Desktop.Tests.TestSupport;

namespace PzLauncher.Desktop.Tests.Localization;

[Collection(StringsCultureCollection.Name)]
public class LocalizationServiceTests : StringsCultureTest
{
    [Theory]
    [MemberData(nameof(TestLanguages.All), MemberType = typeof(TestLanguages))]
    public void Constructor_Language_AppliesToStrings(AppLanguage language)
    {
        var service = new LocalizationService(language);

        Assert.Equal(language, service.Current);
        Assert.Equal(language.CultureName(), Strings.Culture?.Name);
    }

    [Fact]
    public void SetLanguage_Different_SwitchesStringsAndRaisesOnce()
    {
        var raised = new List<AppLanguage>();
        Localization.LanguageChanged += (_, _) => raised.Add(Localization.Current);

        Localization.SetLanguage(AppLanguage.TraditionalChinese);

        Assert.Equal([AppLanguage.TraditionalChinese], raised);
        Assert.Equal("zh-Hant", Strings.Culture?.Name);
    }

    [Fact]
    public void SetLanguage_Same_DoesNotRaise()
    {
        var raised = 0;
        Localization.LanguageChanged += (_, _) => raised++;

        Localization.SetLanguage(AppLanguage.English);

        Assert.Equal(0, raised);
    }
}
