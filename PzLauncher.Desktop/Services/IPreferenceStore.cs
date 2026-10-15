using System.Threading;
using System.Threading.Tasks;
using PzLauncher.Desktop.Localization;
using PzLauncher.Desktop.Themes;

namespace PzLauncher.Desktop.Services;

/// <summary>A <c>null</c> field means no usable saved choice.</summary>
public sealed record UserPreferences(AppLanguage? Language, AppTheme? Theme);

public interface IPreferenceStore
{
    /// <summary>Synchronous: it runs before the first window exists.</summary>
    UserPreferences Load();

    /// <summary>Never throws for I/O failures; they are logged. Keeps the other saved fields.</summary>
    Task SaveLanguageAsync(AppLanguage language, CancellationToken ct = default);

    /// <inheritdoc cref="SaveLanguageAsync"/>
    Task SaveThemeAsync(AppTheme theme, CancellationToken ct = default);
}
