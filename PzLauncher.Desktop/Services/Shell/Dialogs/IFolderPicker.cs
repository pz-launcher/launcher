using System.Threading;
using System.Threading.Tasks;

namespace PzLauncher.Desktop.Services.Shell.Dialogs;

public interface IFolderPicker
{
    Task<FolderPickResult> PickFolderAsync(string title, CancellationToken ct = default);
}
