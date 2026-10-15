using PzLauncher.Desktop.Lang;
using System;

namespace PzLauncher.Desktop.Localization;

public interface ILocalizationService
{
    AppLanguage Current { get; }

    /// <summary>Raised synchronously, on the caller's thread, after <see cref="UiText"/> switched language.</summary>
    event EventHandler? LanguageChanged;

    void SetLanguage(AppLanguage language);
}
