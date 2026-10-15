using PzLauncher.Desktop.Themes;

namespace PzLauncher.Desktop.Services;

public interface IThemeService
{
    AppTheme Current { get; }

    void SetTheme(AppTheme theme);
}
