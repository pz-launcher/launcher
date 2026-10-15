using System;
using System.Globalization;
using System.Linq;

namespace PzLauncher.Desktop.Localization;

public enum AppLanguage
{
    English,
    SimplifiedChinese,
    TraditionalChinese,
    SpanishSpain,
    SpanishMexico,
    SpanishChile,
    SpanishArgentina,
    PortuguesePortugal,
    PortugueseBrazil,
    Russian,
}

public static class AppLanguages
{
    private static readonly string[] TraditionalChineseCultures = ["zh-Hant", "zh-TW", "zh-HK", "zh-MO"];

    /// <summary>Also the value persisted in the preference file: never change one.</summary>
    public static string CultureName(this AppLanguage language) => language switch
    {
        AppLanguage.English => "en",
        AppLanguage.SimplifiedChinese => "zh-Hans",
        AppLanguage.TraditionalChinese => "zh-Hant",
        AppLanguage.SpanishSpain => "es-ES",
        AppLanguage.SpanishMexico => "es-MX",
        AppLanguage.SpanishChile => "es-CL",
        AppLanguage.SpanishArgentina => "es-AR",
        AppLanguage.PortuguesePortugal => "pt-PT",
        AppLanguage.PortugueseBrazil => "pt-BR",
        AppLanguage.Russian => "ru",
    };

    // Not from resx: each option is shown in its own language whatever the current UI language.
    public static string NativeName(this AppLanguage language) => language switch
    {
        AppLanguage.English => "English",
        AppLanguage.SimplifiedChinese => "简体中文",
        AppLanguage.TraditionalChinese => "繁體中文",
        AppLanguage.SpanishSpain => "Español (España)",
        AppLanguage.SpanishMexico => "Español (México)",
        AppLanguage.SpanishChile => "Español (Chile)",
        AppLanguage.SpanishArgentina => "Español (Argentina)",
        AppLanguage.PortuguesePortugal => "Português (Portugal)",
        AppLanguage.PortugueseBrazil => "Português (Brasil)",
        AppLanguage.Russian => "Русский",
    };

    public static AppLanguage? FromCultureName(string? cultureName)
    {
        foreach (var language in Enum.GetValues<AppLanguage>())
        {
            if (string.Equals(language.CultureName(), cultureName, StringComparison.OrdinalIgnoreCase))
            {
                return language;
            }
        }

        return null;
    }

    public static AppLanguage FromCulture(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);

        // Matches names along the parent chain: ICU and NLS disagree on some parents (zh-TW → zh-Hant vs zh-CHT).
        for (var current = culture; current.Name.Length > 0; current = current.Parent)
        {
            if (TraditionalChineseCultures.Contains(current.Name, StringComparer.OrdinalIgnoreCase))
            {
                return AppLanguage.TraditionalChinese;
            }

            if (FromCultureName(current.Name) is { } exact)
            {
                return exact;
            }
        }

        return culture.TwoLetterISOLanguageName switch
        {
            "zh" => AppLanguage.SimplifiedChinese,
            "es" => AppLanguage.SpanishMexico,
            // Bare "pt" is Brazilian in .NET; the other regions (Africa, Asia) follow European norms.
            "pt" => culture.Name.Equals("pt", StringComparison.OrdinalIgnoreCase)
                ? AppLanguage.PortugueseBrazil
                : AppLanguage.PortuguesePortugal,
            "ru" => AppLanguage.Russian,
            _ => AppLanguage.English,
        };
    }
}
