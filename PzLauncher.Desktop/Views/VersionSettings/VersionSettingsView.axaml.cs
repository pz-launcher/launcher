using Avalonia.Controls;
using Avalonia.Interactivity;
using PzLauncher.Desktop.ViewModels.VersionSettings;

namespace PzLauncher.Desktop.Views.VersionSettings;

public partial class VersionSettingsView : UserControl
{
    public VersionSettingsView()
    {
        InitializeComponent();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        if (DataContext is VersionSettingsViewModel vm && vm.ActivateCommand.CanExecute(null))
        {
            vm.ActivateCommand.Execute(null);
        }
    }
}
