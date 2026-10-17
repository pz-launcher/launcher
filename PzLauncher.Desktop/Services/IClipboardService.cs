using System.Threading.Tasks;

namespace PzLauncher.Desktop.Services;

public interface IClipboardService
{
    Task SetTextAsync(string text);

    /// <summary><c>null</c> when the clipboard holds no text or can't be read.</summary>
    Task<string?> GetTextAsync();
}
