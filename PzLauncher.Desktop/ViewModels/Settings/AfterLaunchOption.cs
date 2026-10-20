using CommunityToolkit.Mvvm.ComponentModel;
using PzLauncher.Desktop.Services.Launch;
using PzLauncher.Desktop.Services.Shell;

namespace PzLauncher.Desktop.ViewModels.Settings;

/// <summary>One radio of the Settings after-launch choice: checked only for the saved choice, so none is while nothing is saved.</summary>
public sealed class AfterLaunchOption(AfterLaunchSettings settings, ChoiceOption<AfterLaunchAction> option) : ObservableObject
{
    public AfterLaunchAction Value => option.Value;

    public string Label => option.Text;

    // A radio unchecked by its group pushes false; only checking one changes the choice.
    public bool IsChecked
    {
        get => settings.Action == option.Value;
        set
        {
            if (value)
            {
                settings.Action = option.Value;
            }
        }
    }

    internal void NotifyChecked() => OnPropertyChanged(nameof(IsChecked));
}
