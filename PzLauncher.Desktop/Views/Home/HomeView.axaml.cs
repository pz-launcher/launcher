using System;
using Avalonia.Controls;
using PzLauncher.Desktop.ViewModels.Home;

namespace PzLauncher.Desktop.Views.Home;

public partial class HomeView : UserControl
{
    public HomeView()
    {
        InitializeComponent();
    }

    private void OnCollectionsDropDownOpened(object? sender, EventArgs e)
    {
        if (DataContext is HomeViewModel vm && vm.RefreshCollectionsCommand.CanExecute(null))
        {
            vm.RefreshCollectionsCommand.Execute(null);
        }
    }
}
