using System.Threading;
using System.Threading.Tasks;
using PzLauncher.Desktop.Localization;

namespace PzLauncher.Desktop.Services;

public interface ILanguagePreferenceStore
{
    /// <summary>Synchronous: it runs before the first window exists. <c>null</c> means no usable saved choice.</summary>
    AppLanguage? Load();

    /// <summary>Never throws for I/O failures; they are logged.</summary>
    Task SaveAsync(AppLanguage language, CancellationToken ct = default);
}
