using Avalonia.Controls;
using Avalonia.Interactivity;
using PzLauncher.Desktop.ViewModels.LaunchSettings;

namespace PzLauncher.Desktop.Views.LaunchSettings;

public partial class LaunchSettingsView : UserControl
{
    public LaunchSettingsView()
    {
        InitializeComponent();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        if (DataContext is LaunchSettingsViewModel vm && vm.ActivateCommand.CanExecute(null))
        {
            vm.ActivateCommand.Execute(null);
        }
    }
}
