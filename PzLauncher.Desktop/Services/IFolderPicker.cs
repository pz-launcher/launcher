using System.Threading;
using System.Threading.Tasks;

namespace PzLauncher.Desktop.Services;

public interface IFolderPicker
{
    Task<FolderPickResult> PickFolderAsync(string title, CancellationToken ct = default);
}
