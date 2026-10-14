using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using PzLauncher.Desktop.Localization;
using PzLauncher.Desktop.Services;
using PzLauncher.Desktop.ViewModels.Installs;

namespace PzLauncher.Desktop.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly ILocalizationService _localization;
    private readonly ILanguagePreferenceStore _preferences;

    public MainViewModel(InstallsViewModel installs, ILocalizationService localization, ILanguagePreferenceStore preferences)
    {
        _localization = localization;
        _preferences = preferences;
        CurrentPage = installs;
        SelectedLanguage = Languages.First(option => option.Language == localization.Current);
    }

    public ViewModelBase CurrentPage { get; }

    public IReadOnlyList<LanguageOption> Languages { get; } =
        [.. Enum.GetValues<AppLanguage>().Select(language => new LanguageOption(language, language.NativeName()))];

    [ObservableProperty] public partial LanguageOption SelectedLanguage { get; set; }

    partial void OnSelectedLanguageChanged(LanguageOption value)
    {
        // The two-way SelectedItem binding can push null while the replaced MainView's ComboBox is torn down.
        if (value is null || value.Language == _localization.Current)
        {
            return;
        }

        _localization.SetLanguage(value.Language);

        // SaveAsync never throws for I/O, and a failed save must not undo the switch.
        _ = _preferences.SaveAsync(value.Language);
    }
}
