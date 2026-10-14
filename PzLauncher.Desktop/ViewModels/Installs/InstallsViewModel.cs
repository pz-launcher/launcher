using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PzLauncher.Core.Installs;
using PzLauncher.Core.Platform;
using PzLauncher.Desktop.Localization;
using PzLauncher.Desktop.Services;

namespace PzLauncher.Desktop.ViewModels.Installs;

/// <summary>Manually added folders last for this session only.</summary>
public partial class InstallsViewModel(
    IInstallDetectionService installDetection,
    IFolderPicker folderPicker,
    IPlatformEnvironment platform) : ViewModelBase
{
    private readonly List<PzInstall> _manualInstalls = [];
    private bool _activated;

    public ObservableCollection<PzInstall> Installs { get; } = [];

    public ObservableCollection<string> Warnings { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial bool IsScanning { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial bool HasScanned { get; private set; }

    [ObservableProperty] public partial string? ErrorText { get; private set; }

    public bool IsEmpty => HasScanned && !IsScanning && Installs.Count == 0;

    // The view calls this on every load; only the first call scans.
    [RelayCommand]
    private async Task ActivateAsync()
    {
        if (_activated)
        {
            return;
        }

        _activated = true;
        await ScanCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private async Task ScanAsync(CancellationToken ct)
    {
        IsScanning = true;
        ErrorText = null;
        try
        {
            var result = await installDetection.ScanAsync(ct);
            Warnings.Clear();
            if (!result.IsSuccess)
            {
                ErrorText = ErrorMessages.ToUserText(result.Error);
                return;
            }

            Installs.Clear();
            foreach (var install in PzInstallList.Merge(result.Value.Installs, _manualInstalls, platform.PathComparison))
            {
                Installs.Add(install);
            }

            foreach (var warning in result.Value.Warnings)
            {
                Warnings.Add(ErrorMessages.ToUserText(warning));
            }

            HasScanned = true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Cancellation isn't an error: go idle and keep what's shown.
        }
        finally
        {
            IsScanning = false;
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    [RelayCommand]
    private async Task AddFolderAsync(CancellationToken ct)
    {
        try
        {
            string path;
            switch (await folderPicker.PickFolderAsync("Select a Project Zomboid game or server folder", ct))
            {
                case FolderPickResult.Picked picked:
                    path = picked.Path;
                    break;
                case FolderPickResult.Cancelled:
                    return;
                case FolderPickResult.NotLocal notLocal:
                    ErrorText = ErrorMessages.FolderNotLocal(notLocal.Location);
                    return;
                case var other:
                    throw new UnreachableException($"Unhandled {nameof(FolderPickResult)}: {other}");
            }

            ErrorText = null;
            var result = await installDetection.InspectAsync(path, ct);
            if (!result.IsSuccess)
            {
                ErrorText = ErrorMessages.ToUserText(result.Error);
                return;
            }

            if (PzInstallList.ContainsPath(Installs, result.Value.RootPath, platform.PathComparison))
            {
                return;
            }

            _manualInstalls.Add(result.Value);
            Installs.Add(result.Value);
            OnPropertyChanged(nameof(IsEmpty));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Cancelled: no change.
        }
    }
}
