using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PzLauncher.Desktop.Localization;

namespace PzLauncher.Desktop.ViewModels;

public sealed partial class LanguageOption : ObservableObject
{
    private readonly Action<AppLanguage> _select;

    internal LanguageOption(AppLanguage language, Action<AppLanguage> select)
    {
        Language = language;
        _select = select;
    }

    public AppLanguage Language { get; }

    public string NativeName => Language.NativeName();

    [ObservableProperty] public partial bool IsCurrent { get; internal set; }

    [RelayCommand]
    private void Select() => _select(Language);
}
