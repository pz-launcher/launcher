using System;
using System.Globalization;
using System.Linq;

namespace PzLauncher.Desktop.Localization;

public enum AppLanguage
{
    English,
    SimplifiedChinese,
    TraditionalChinese,
    Spanish,
    Russian,
    Portuguese,
}

#pragma warning disable CS8524 // No discard arms on purpose: CS8509 makes a new AppLanguage without a mapping a build error.
public static class AppLanguages
{
    private static readonly string[] TraditionalChineseCultures = ["zh-Hant", "zh-TW", "zh-HK", "zh-MO"];

    /// <summary>Also the value persisted in the preference file: never change one.</summary>
    public static string CultureName(this AppLanguage language) => language switch
    {
        AppLanguage.English => "en",
        AppLanguage.SimplifiedChinese => "zh-Hans",
        AppLanguage.TraditionalChinese => "zh-Hant",
        AppLanguage.Spanish => "es",
        AppLanguage.Russian => "ru",
        AppLanguage.Portuguese => "pt",
    };

    // Not from resx: each option is shown in its own language whatever the current UI language.
    public static string NativeName(this AppLanguage language) => language switch
    {
        AppLanguage.English => "English",
        AppLanguage.SimplifiedChinese => "简体中文",
        AppLanguage.TraditionalChinese => "繁體中文",
        AppLanguage.Spanish => "Español",
        AppLanguage.Russian => "Русский",
        AppLanguage.Portuguese => "Português (Brasil)",
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
        }

        return culture.TwoLetterISOLanguageName switch
        {
            "zh" => AppLanguage.SimplifiedChinese,
            "es" => AppLanguage.Spanish,
            "ru" => AppLanguage.Russian,
            "pt" => AppLanguage.Portuguese,
            _ => AppLanguage.English,
        };
    }
}
#pragma warning restore CS8524
