using PzLauncher.Desktop.ViewModels.Installs;

namespace PzLauncher.Desktop.ViewModels;

public partial class MainViewModel(InstallsViewModel installs) : ViewModelBase
{
    public ViewModelBase CurrentPage { get; } = installs;
}
