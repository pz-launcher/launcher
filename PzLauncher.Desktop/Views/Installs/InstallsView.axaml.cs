using Avalonia.Controls;
using Avalonia.Interactivity;
using PzLauncher.Desktop.ViewModels.Installs;

namespace PzLauncher.Desktop.Views.Installs;

public partial class InstallsView : UserControl
{
    public InstallsView()
    {
        InitializeComponent();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        if (DataContext is InstallsViewModel vm && vm.ActivateCommand.CanExecute(null))
        {
            vm.ActivateCommand.Execute(null);
        }
    }
}
