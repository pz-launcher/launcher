using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PzLauncher.Core.Java;
using PzLauncher.Core.Platform;
using PzLauncher.Desktop.Lang;
using PzLauncher.Desktop.Localization;
using PzLauncher.Desktop.Services.Preferences;
using PzLauncher.Desktop.Services.Shell.Dialogs;
using PzLauncher.Desktop.Services.Shell;

namespace PzLauncher.Desktop.ViewModels.Java;

/// <summary>The app-wide Java list for the session: scanned runtimes plus the user's persisted manual folders.</summary>
public partial class JavaRuntimesViewModel : ViewModelBase
{
    private readonly IJavaDiscoveryService _discovery;
    private readonly IFolderPicker _folderPicker;
    private readonly IPreferenceStore _preferences;
    private readonly StringComparer _pathComparer;
    private readonly List<string> _manualHomes;

    // Latest inspection per manual home (null: no longer a usable Java). Homes not inspected yet aren't listed.
    private readonly Dictionary<string, JavaRuntime?> _manualRuntimes;
    private IReadOnlyList<JavaRuntime> _scanned = [];

    private readonly LocalizedErrorList _warnings = new();
    private readonly LocalizedText _error;

    private Task? _firstScan;

    public JavaRuntimesViewModel(
        IJavaDiscoveryService discovery,
        IFolderPicker folderPicker,
        IPreferenceStore preferences,
        UserPreferences savedPreferences,
        IPlatformEnvironment platform,
        ILocalizationService localization)
    {
        _discovery = discovery;
        _folderPicker = folderPicker;
        _preferences = preferences;
        _pathComparer = StringComparer.FromComparison(platform.PathComparison);
        _manualHomes = [.. savedPreferences.ManualJavaHomes.Distinct(_pathComparer)];
        _manualRuntimes = new Dictionary<string, JavaRuntime?>(_pathComparer);
        _error = new LocalizedText(text => ErrorText = text);

        // Never unsubscribed: this view model is an app-lifetime singleton.
        localization.LanguageChanged += (_, _) => RenderTexts();
    }

    public ObservableCollection<JavaRuntimeRow> Runtimes { get; } = [];

    public ObservableCollection<string> Warnings => _warnings.Items;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial bool IsScanning { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial bool HasScanned { get; private set; }

    [ObservableProperty] public partial string? ErrorText { get; private set; }

    public bool IsEmpty => HasScanned && !IsScanning && Runtimes.Count == 0;

    /// <summary>The selectable runtimes (missing manual folders excluded).</summary>
    public IReadOnlyList<JavaRuntime> AvailableRuntimes =>
        [.. Runtimes.Select(row => row.Runtime).OfType<JavaRuntime>()];

    /// <summary>Scans the first time it's called in a session; later calls return that scan.</summary>
    public Task EnsureScannedAsync() => _firstScan ??= ScanCommand.ExecuteAsync(null);

    [RelayCommand]
    private async Task ScanAsync(CancellationToken ct)
    {
        IsScanning = true;
        _error.Clear();
        try
        {
            var result = await _discovery.ScanAsync(ct);
            _warnings.Clear();
            if (!result.IsSuccess)
            {
                _error.Show(result.Error);
                return;
            }

            foreach (var home in _manualHomes.ToList())
            {
                var inspected = await _discovery.InspectAsync(home, ct);
                _manualRuntimes[home] = inspected.IsSuccess ? inspected.Value : null;
            }

            _scanned = result.Value.Runtimes;
            foreach (var warning in result.Value.Warnings)
            {
                _warnings.Add(warning);
            }

            RebuildRows();
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
    private async Task AddAsync(CancellationToken ct)
    {
        try
        {
            string path;
            switch (await _folderPicker.PickFolderAsync(UiText.Java_PickFolderTitle, ct))
            {
                case FolderPickResult.Picked picked:
                    path = picked.Path;
                    break;
                case FolderPickResult.Cancelled:
                    return;
                case FolderPickResult.NotLocal notLocal:
                    _error.Show(() => ErrorMessages.FolderNotLocal(notLocal.Location));
                    return;
                case var other:
                    throw new UnreachableException($"Unhandled {nameof(FolderPickResult)}: {other}");
            }

            _error.Clear();
            var result = await _discovery.InspectAsync(path, ct);
            if (!result.IsSuccess)
            {
                _error.Show(result.Error);
                return;
            }

            // Re-adding a listed folder refreshes it, so a missing one that is back shows as available again.
            var home = result.Value.HomePath;
            _manualRuntimes[home] = result.Value;
            if (!_manualHomes.Contains(home, _pathComparer))
            {
                _manualHomes.Add(home);
                SaveManualHomes();
            }

            RebuildRows();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Cancelled: no change.
        }
    }

    private void Remove(JavaRuntimeRow row)
    {
        if (!row.IsManual)
        {
            return;
        }

        _manualHomes.RemoveAll(home => _pathComparer.Equals(home, row.HomePath));
        _manualRuntimes.Remove(row.HomePath);
        SaveManualHomes();
        RebuildRows();
    }

    // SaveManualJavaHomesAsync never throws for I/O, and a failed save must not undo the change.
    private void SaveManualHomes() => _ = _preferences.SaveManualJavaHomesAsync([.. _manualHomes]);

    // A manual home the scan also found takes the scanned row's place, shown as manual; removing it shows the scanned row again.
    private void RebuildRows()
    {
        var scannedByHome = new Dictionary<string, JavaRuntime>(_pathComparer);
        foreach (var runtime in _scanned)
        {
            scannedByHome.TryAdd(runtime.HomePath, runtime);
        }

        var listedManual = new HashSet<string>(_manualHomes.Where(_manualRuntimes.ContainsKey), _pathComparer);
        var placed = new HashSet<string>(_pathComparer);
        Runtimes.Clear();
        foreach (var runtime in _scanned)
        {
            if (!listedManual.Contains(runtime.HomePath))
            {
                Runtimes.Add(new JavaRuntimeRow(runtime.HomePath, runtime, isManual: false, Remove));
            }
            else if (placed.Add(runtime.HomePath))
            {
                Runtimes.Add(ManualRow(runtime.HomePath, runtime));
            }
        }

        foreach (var home in _manualHomes)
        {
            if (listedManual.Contains(home) && placed.Add(home))
            {
                Runtimes.Add(ManualRow(home, scannedByHome.GetValueOrDefault(home)));
            }
        }

        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(AvailableRuntimes));
    }

    private JavaRuntimeRow ManualRow(string home, JavaRuntime? scanned)
    {
        var runtime = _manualRuntimes[home] ?? scanned;
        return new JavaRuntimeRow(home, runtime is null ? null : runtime with { Source = JavaRuntimeSource.Manual }, isManual: true, Remove);
    }

    private void RenderTexts()
    {
        _error.Render();
        _warnings.Render();
    }
}
