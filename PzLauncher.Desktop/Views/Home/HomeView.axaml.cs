using Avalonia.Controls;
using Avalonia.Interactivity;
using PzLauncher.Desktop.ViewModels.Home;

namespace PzLauncher.Desktop.Views.Home;

public partial class HomeView : UserControl
{
    public HomeView()
    {
        InitializeComponent();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        if (DataContext is HomeViewModel vm && vm.ActivateCommand.CanExecute(null))
        {
            vm.ActivateCommand.Execute(null);
        }
    }
}
