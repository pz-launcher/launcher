using System.Threading.Tasks;

namespace PzLauncher.Desktop.Services;

public interface IDialogService
{
    /// <summary>Modal over the main window; completes when the user closes it.</summary>
    Task ShowErrorAsync(string message);
}
