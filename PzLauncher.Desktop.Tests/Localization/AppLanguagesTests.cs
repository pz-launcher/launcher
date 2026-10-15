using System.Globalization;
using PzLauncher.Desktop.Localization;
using PzLauncher.Desktop.Tests.TestSupport;

namespace PzLauncher.Desktop.Tests.Localization;

public class AppLanguagesTests
{
    [Theory]
    [InlineData("zh-CN", AppLanguage.SimplifiedChinese)]
    [InlineData("zh-SG", AppLanguage.SimplifiedChinese)]
    [InlineData("zh-Hans", AppLanguage.SimplifiedChinese)]
    [InlineData("zh", AppLanguage.SimplifiedChinese)]
    [InlineData("zh-TW", AppLanguage.TraditionalChinese)]
    [InlineData("zh-HK", AppLanguage.TraditionalChinese)]
    [InlineData("zh-MO", AppLanguage.TraditionalChinese)]
    [InlineData("zh-Hant", AppLanguage.TraditionalChinese)]
    [InlineData("es-ES", AppLanguage.Spanish)]
    [InlineData("es-MX", AppLanguage.Spanish)]
    [InlineData("es-419", AppLanguage.Spanish)]
    [InlineData("ru-RU", AppLanguage.Russian)]
    [InlineData("pt-BR", AppLanguage.Portuguese)]
    [InlineData("pt-PT", AppLanguage.Portuguese)]
    [InlineData("en-US", AppLanguage.English)]
    [InlineData("ja-JP", AppLanguage.English)]
    [InlineData("", AppLanguage.English)]
    public void FromCulture_OsCulture_MapsToSupportedLanguage(string cultureName, AppLanguage expected) =>
        Assert.Equal(expected, AppLanguages.FromCulture(CultureInfo.GetCultureInfo(cultureName)));

    [Theory]
    [MemberData(nameof(TestLanguages.All), MemberType = typeof(TestLanguages))]
    public void FromCultureName_CultureNameOfLanguage_RoundTrips(AppLanguage language) =>
        Assert.Equal(language, AppLanguages.FromCultureName(language.CultureName()));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("fr")]
    [InlineData("zh-CN")]
    [InlineData("pt-BR")]
    [InlineData("SimplifiedChinese")]
    public void FromCultureName_Unknown_ReturnsNull(string? cultureName) =>
        Assert.Null(AppLanguages.FromCultureName(cultureName));

    [Theory]
    [InlineData(AppLanguage.English, "en", "English")]
    [InlineData(AppLanguage.SimplifiedChinese, "zh-Hans", "简体中文")]
    [InlineData(AppLanguage.TraditionalChinese, "zh-Hant", "繁體中文")]
    [InlineData(AppLanguage.Spanish, "es", "Español")]
    [InlineData(AppLanguage.Russian, "ru", "Русский")]
    [InlineData(AppLanguage.Portuguese, "pt", "Português (Brasil)")]
    public void CultureNameAndNativeName_EveryLanguage_AreFixed(AppLanguage language, string cultureName, string nativeName)
    {
        Assert.Equal(cultureName, language.CultureName());
        Assert.Equal(nativeName, language.NativeName());
    }
}
