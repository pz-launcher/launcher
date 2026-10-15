using PzLauncher.Desktop.Lang;
using System;
using System.Globalization;

namespace PzLauncher.Desktop.Localization;

/// <summary>The only writer of <see cref="UiText.Culture"/>.</summary>
public sealed class LocalizationService : ILocalizationService
{
    public LocalizationService(AppLanguage language)
    {
        Current = language;
        Apply(language);
    }

    public AppLanguage Current { get; private set; }

    public event EventHandler? LanguageChanged;

    public void SetLanguage(AppLanguage language)
    {
        if (language == Current)
        {
            return;
        }

        Current = language;
        Apply(language);
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    // UiText.Culture rather than CurrentUICulture: the latter flows with ExecutionContext, so an await
    // started before a switch would resume rendering in the old language.
    private static void Apply(AppLanguage language) =>
        UiText.Culture = CultureInfo.GetCultureInfo(language.CultureName());
}
