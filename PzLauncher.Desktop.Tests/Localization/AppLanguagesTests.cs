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
    [InlineData("es-ES", AppLanguage.SpanishSpain)]
    [InlineData("es-MX", AppLanguage.SpanishMexico)]
    [InlineData("es-419", AppLanguage.SpanishMexico)]
    [InlineData("es-CO", AppLanguage.SpanishMexico)]
    [InlineData("es-US", AppLanguage.SpanishMexico)]
    [InlineData("es", AppLanguage.SpanishMexico)]
    [InlineData("es-CL", AppLanguage.SpanishChile)]
    [InlineData("es-AR", AppLanguage.SpanishArgentina)]
    [InlineData("pt-BR", AppLanguage.PortugueseBrazil)]
    [InlineData("pt", AppLanguage.PortugueseBrazil)]
    [InlineData("pt-PT", AppLanguage.PortuguesePortugal)]
    [InlineData("pt-AO", AppLanguage.PortuguesePortugal)]
    [InlineData("ru-RU", AppLanguage.Russian)]
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
    [InlineData("es")]
    [InlineData("pt")]
    [InlineData("es-419")]
    [InlineData("SimplifiedChinese")]
    public void FromCultureName_Unknown_ReturnsNull(string? cultureName) =>
        Assert.Null(AppLanguages.FromCultureName(cultureName));

    [Theory]
    [InlineData(AppLanguage.English, "en", "English")]
    [InlineData(AppLanguage.SimplifiedChinese, "zh-Hans", "简体中文")]
    [InlineData(AppLanguage.TraditionalChinese, "zh-Hant", "繁體中文")]
    [InlineData(AppLanguage.SpanishSpain, "es-ES", "Español (España)")]
    [InlineData(AppLanguage.SpanishMexico, "es-MX", "Español (México)")]
    [InlineData(AppLanguage.SpanishChile, "es-CL", "Español (Chile)")]
    [InlineData(AppLanguage.SpanishArgentina, "es-AR", "Español (Argentina)")]
    [InlineData(AppLanguage.PortuguesePortugal, "pt-PT", "Português (Portugal)")]
    [InlineData(AppLanguage.PortugueseBrazil, "pt-BR", "Português (Brasil)")]
    [InlineData(AppLanguage.Russian, "ru", "Русский")]
    public void CultureNameAndNativeName_EveryLanguage_AreFixed(AppLanguage language, string cultureName, string nativeName)
    {
        Assert.Equal(cultureName, language.CultureName());
        Assert.Equal(nativeName, language.NativeName());
    }
}
