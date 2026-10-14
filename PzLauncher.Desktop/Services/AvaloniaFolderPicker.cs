using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace PzLauncher.Desktop.Services;

public sealed class AvaloniaFolderPicker(Func<TopLevel?> topLevel) : IFolderPicker
{
    public async Task<FolderPickResult> PickFolderAsync(string title, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var top = topLevel() ?? throw new InvalidOperationException("The folder picker was used before the main window exists.");

        var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
        });

        if (folders.Count == 0)
        {
            return new FolderPickResult.Cancelled();
        }

        var folder = folders[0];
        return folder.TryGetLocalPath() is { } path
            ? new FolderPickResult.Picked(path)
            : new FolderPickResult.NotLocal(folder.Path.ToString());
    }
}
