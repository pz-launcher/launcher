using Avalonia;
using Avalonia.Styling;
using PzLauncher.Desktop.Themes;

namespace PzLauncher.Desktop.Services;

public sealed class AvaloniaThemeService : IThemeService
{
    private readonly Application _application;

    public AvaloniaThemeService(Application application, AppTheme initial)
    {
        _application = application;
        Current = initial;
        Apply(initial);
    }

    public AppTheme Current { get; private set; }

    public void SetTheme(AppTheme theme)
    {
        Current = theme;
        Apply(theme);
    }

    private void Apply(AppTheme theme) =>
        _application.RequestedThemeVariant = theme switch
        {
            AppTheme.Light => ThemeVariant.Light,
            AppTheme.Dark => ThemeVariant.Dark,
        };
}
