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

#pragma warning disable CS8524 // No discard arm on purpose: CS8509 makes a new AppTheme without a variant a build error.
    private void Apply(AppTheme theme) =>
        _application.RequestedThemeVariant = theme switch
        {
            AppTheme.Light => ThemeVariant.Light,
            AppTheme.Dark => ThemeVariant.Dark,
        };
#pragma warning restore CS8524
}
