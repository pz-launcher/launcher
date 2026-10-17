using Avalonia.Controls;
using Avalonia.Interactivity;
using PzLauncher.Desktop.ViewModels.Mods;

namespace PzLauncher.Desktop.Views.Mods;

public partial class ModsView : UserControl
{
    public ModsView()
    {
        InitializeComponent();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        if (DataContext is ModsViewModel vm && vm.ActivateCommand.CanExecute(null))
        {
            vm.ActivateCommand.Execute(null);
        }
    }
}
