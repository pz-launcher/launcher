using System;

namespace PzLauncher.Desktop.Localization;

public interface ILocalizationService
{
    AppLanguage Current { get; }

    /// <summary>Raised synchronously, on the caller's thread, after <see cref="Strings"/> switched language.</summary>
    event EventHandler? LanguageChanged;

    void SetLanguage(AppLanguage language);
}
