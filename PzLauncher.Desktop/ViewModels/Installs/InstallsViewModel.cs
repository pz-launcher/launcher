using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PzLauncher.Core.Common;
using PzLauncher.Core.Installs;
using PzLauncher.Core.Platform;
using PzLauncher.Desktop.Lang;
using PzLauncher.Desktop.Localization;
using PzLauncher.Desktop.Services.Shell;

namespace PzLauncher.Desktop.ViewModels.Installs;

/// <summary>Manually added folders last for this session only.</summary>
public partial class InstallsViewModel : ViewModelBase
{
    private readonly IInstallDetectionService _installDetection;
    private readonly IFolderPicker _folderPicker;
    private readonly IPlatformEnvironment _platform;
    private readonly List<PzInstall> _manualInstalls = [];

    // Sources of the shown texts, kept so a language switch can re-render them.
    private readonly List<Error> _warningSources = [];
    private Func<string>? _errorSource;

    private bool _activated;

    public InstallsViewModel(
        IInstallDetectionService installDetection,
        IFolderPicker folderPicker,
        IPlatformEnvironment platform,
        ILocalizationService localization)
    {
        _installDetection = installDetection;
        _folderPicker = folderPicker;
        _platform = platform;
        VisibleInstalls = [];

        // Never unsubscribed: this view model is an app-lifetime singleton.
        localization.LanguageChanged += (_, _) => RenderTexts();
    }

    public ObservableCollection<PzInstall> Installs { get; } = [];

    public event EventHandler? HomeRequested;

    public event EventHandler<PzInstall>? LaunchSettingsRequested;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFilterEmpty))]
    public partial IReadOnlyList<InstallRow> VisibleInstalls { get; private set; }

    [ObservableProperty] public partial InstallsFilter Filter { get; set; } = InstallsFilter.Game;

    /// <summary>The install in use, session only. Only a scan and <see cref="Use"/> change it; the list never does.</summary>
    [ObservableProperty] public partial PzInstall? SelectedInstall { get; private set; }

    public ObservableCollection<string> Warnings { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty), nameof(IsFilterEmpty))]
    public partial bool IsScanning { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty), nameof(IsFilterEmpty))]
    public partial bool HasScanned { get; private set; }

    [ObservableProperty] public partial string? ErrorText { get; private set; }

    public bool IsEmpty => HasScanned && !IsScanning && Installs.Count == 0;

    public bool IsFilterEmpty => HasScanned && !IsScanning && Installs.Count > 0 && VisibleInstalls.Count == 0;

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
        ShowError(null);
        try
        {
            var result = await _installDetection.ScanAsync(ct);
            _warningSources.Clear();
            Warnings.Clear();
            if (!result.IsSuccess)
            {
                var error = result.Error;
                ShowError(() => ErrorMessages.ToUserText(error));
                return;
            }

            var previous = SelectedInstall;
            Installs.Clear();
            foreach (var install in PzInstallList.Merge(result.Value.Installs, _manualInstalls, _platform.PathComparison))
            {
                Installs.Add(install);
            }

            SelectedInstall = (previous is null ? null : FindByPath(previous.RootPath)) ?? Installs.FirstOrDefault();
            RefreshVisibleInstalls();

            foreach (var warning in result.Value.Warnings)
            {
                _warningSources.Add(warning);
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
            switch (await _folderPicker.PickFolderAsync(UiText.Installs_PickFolderTitle, ct))
            {
                case FolderPickResult.Picked picked:
                    path = picked.Path;
                    break;
                case FolderPickResult.Cancelled:
                    return;
                case FolderPickResult.NotLocal notLocal:
                    ShowError(() => ErrorMessages.FolderNotLocal(notLocal.Location));
                    return;
                case var other:
                    throw new UnreachableException($"Unhandled {nameof(FolderPickResult)}: {other}");
            }

            ShowError(null);
            var result = await _installDetection.InspectAsync(path, ct);
            if (!result.IsSuccess)
            {
                var error = result.Error;
                ShowError(() => ErrorMessages.ToUserText(error));
                return;
            }

            if (PzInstallList.ContainsPath(Installs, result.Value.RootPath, _platform.PathComparison))
            {
                return;
            }

            _manualInstalls.Add(result.Value);
            Installs.Add(result.Value);
            RefreshVisibleInstalls();
            OnPropertyChanged(nameof(IsEmpty));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Cancelled: no change.
        }
    }

    /// <summary>Makes the listed install with this root the one in use; an unlisted root changes nothing.</summary>
    public void Use(string rootPath)
    {
        if (FindByPath(rootPath) is { } install)
        {
            SelectedInstall = install;
        }
    }

    public bool IsListed(string rootPath) => FindByPath(rootPath) is not null;

    public bool IsInUse(string rootPath) =>
        SelectedInstall is { } selected && string.Equals(selected.RootPath, rootPath, _platform.PathComparison);

    [RelayCommand]
    private void Choose(PzInstall install)
    {
        Use(install.RootPath);
        HomeRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void OpenSettings(PzInstall install) => LaunchSettingsRequested?.Invoke(this, install);

    partial void OnFilterChanged(InstallsFilter value) => RefreshVisibleInstalls();

    partial void OnSelectedInstallChanged(PzInstall? value) => RefreshVisibleInstalls();

    private PzInstall? FindByPath(string rootPath) =>
        Installs.FirstOrDefault(install => string.Equals(install.RootPath, rootPath, _platform.PathComparison));

    private void RefreshVisibleInstalls() =>
        VisibleInstalls = [.. Installs.Where(Matches).Select(install => new InstallRow(install, install == SelectedInstall))];

    private bool Matches(PzInstall install) => Filter switch
    {
        InstallsFilter.Game => install.Kind == PzInstallKind.Game,
        InstallsFilter.DedicatedServer => install.Kind == PzInstallKind.DedicatedServer,
    };

    private void ShowError(Func<string>? source)
    {
        _errorSource = source;
        ErrorText = source?.Invoke();
    }

    private void RenderTexts()
    {
        ErrorText = _errorSource?.Invoke();
        for (var i = 0; i < _warningSources.Count; i++)
        {
            Warnings[i] = ErrorMessages.ToUserText(_warningSources[i]);
        }
    }
}
