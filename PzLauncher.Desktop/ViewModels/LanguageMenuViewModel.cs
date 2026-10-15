using System;
using System.Collections.Generic;
using System.Linq;
using PzLauncher.Desktop.Localization;
using PzLauncher.Desktop.Services;

namespace PzLauncher.Desktop.ViewModels;

public sealed class LanguageMenuViewModel : ViewModelBase
{
    private readonly ILocalizationService _localization;
    private readonly IPreferenceStore _preferences;

    public LanguageMenuViewModel(ILocalizationService localization, IPreferenceStore preferences)
    {
        _localization = localization;
        _preferences = preferences;
        Languages = [.. Enum.GetValues<AppLanguage>().Select(language => new LanguageOption(language, Select))];
        MarkCurrent();
    }

    public IReadOnlyList<LanguageOption> Languages { get; }

    private void Select(AppLanguage language)
    {
        if (language == _localization.Current)
        {
            return;
        }

        _localization.SetLanguage(language);
        MarkCurrent();

        // SaveLanguageAsync never throws for I/O, and a failed save must not undo the switch.
        _ = _preferences.SaveLanguageAsync(language);
    }

    private void MarkCurrent()
    {
        foreach (var option in Languages)
        {
            option.IsCurrent = option.Language == _localization.Current;
        }
    }
}
