using PzLauncher.Desktop.Lang;
using PzLauncher.Desktop.Localization;
using PzLauncher.Desktop.Tests.TestSupport;

namespace PzLauncher.Desktop.Tests.Localization;

[Collection(UiTextCultureCollection.Name)]
public class LocalizationServiceTests : UiTextCultureTest
{
    [Theory]
    [MemberData(nameof(TestLanguages.All), MemberType = typeof(TestLanguages))]
    public void Constructor_Language_AppliesToUiText(AppLanguage language)
    {
        var service = new LocalizationService(language);

        Assert.Equal(language, service.Current);
        Assert.Equal(language.CultureName(), UiText.Culture?.Name);
    }

    [Fact]
    public void SetLanguage_Different_SwitchesUiTextAndRaisesOnce()
    {
        var raised = new List<AppLanguage>();
        Localization.LanguageChanged += (_, _) => raised.Add(Localization.Current);

        Localization.SetLanguage(AppLanguage.TraditionalChinese);

        Assert.Equal([AppLanguage.TraditionalChinese], raised);
        Assert.Equal("zh-Hant", UiText.Culture?.Name);
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
