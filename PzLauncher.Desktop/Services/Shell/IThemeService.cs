using PzLauncher.Desktop.Themes;

namespace PzLauncher.Desktop.Services.Shell;

public interface IThemeService
{
    AppTheme Current { get; }

    void SetTheme(AppTheme theme);
}
