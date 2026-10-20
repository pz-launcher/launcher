using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using PzLauncher.Core.Common;
using PzLauncher.Core.Update;
using PzLauncher.Desktop.Services.Launch;
using PzLauncher.Desktop.Services.Preferences;
using PzLauncher.Desktop.Services.Shell;

namespace PzLauncher.Desktop.Services.Update;

/// <summary>What the last update activity is doing or found; shown in Settings.</summary>
public enum UpdateStatus
{
    Idle,
    Checking,
    Downloading,
    UpToDate,

    /// <summary>A package is staged; it installs on restart or close.</summary>
    Ready,

    /// <summary>A newer release exists, but the launcher's folder isn't writable.</summary>
    Available,

    /// <summary>Installed, but the new version couldn't be started.</summary>
    Installed,

    /// <summary>A check the user asked for failed; startup failures are only logged.</summary>
    Failed,
}

/// <summary>App-lifetime: runs the update flow; the banner and Settings observe it.</summary>
public sealed partial class UpdateCoordinator : ObservableObject
{
    private readonly IUpdateService _updates;
    private readonly IPreferenceStore _preferences;
    private readonly GameRunState _runState;
    private readonly IAppLifetime _app;

    // Set after the saved choices are applied, so applying them doesn't save them back.
    private readonly bool _loaded;

    private bool _started;

    public UpdateCoordinator(
        IUpdateService updates, IPreferenceStore preferences, UserPreferences saved, GameRunState runState, IAppLifetime app)
    {
        _updates = updates;
        _preferences = preferences;
        _runState = runState;
        _app = app;
        Source = saved.UpdateSource ?? UpdateSource.Auto;
        AutoCheck = saved.AutoCheckUpdates ?? true;
        _loaded = true;

        // Never unsubscribed: both are app-lifetime singletons.
        runState.PropertyChanged += OnRunStateChanged;
    }

    public SemVersion CurrentVersion => _updates.CurrentVersion;

    [ObservableProperty] public partial UpdateSource Source { get; set; }

    /// <summary>Only the check at startup; "Check now" works either way.</summary>
    [ObservableProperty] public partial bool AutoCheck { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy), nameof(CanCheck), nameof(CanRestart))]
    public partial UpdateStatus Status { get; private set; }

    /// <summary>The newer release the last successful check found.</summary>
    [ObservableProperty] public partial ReleaseInfo? Release { get; private set; }

    /// <summary>A newer release that can't be installed in place; offered as a download page instead.</summary>
    [ObservableProperty] public partial ReleaseInfo? Available { get; private set; }

    /// <summary>Waiting to be installed by a restart or the next close.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRestart))]
    public partial StagedUpdate? Staged { get; private set; }

    /// <summary><c>null</c> while the size is unknown.</summary>
    [ObservableProperty] public partial int? DownloadPercent { get; private set; }

    /// <summary>Set with <see cref="UpdateStatus.Failed"/>.</summary>
    [ObservableProperty] public partial Error? Error { get; private set; }

    /// <summary>Set with <see cref="UpdateStatus.Installed"/>.</summary>
    [ObservableProperty] public partial SemVersion? InstalledVersion { get; private set; }

    /// <summary>Why the last "Restart and update" didn't restart.</summary>
    [ObservableProperty] public partial Error? RestartError { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRestart), nameof(CanCheck))]
    public partial bool IsInstalling { get; private set; }

    public bool IsBusy => Status is UpdateStatus.Checking or UpdateStatus.Downloading;

    public bool CanCheck => !IsBusy && !IsInstalling && Status != UpdateStatus.Installed;

    /// <summary>Restarting would lose track of a game started from this session; a running download may replace the staged package.</summary>
    public bool CanRestart => Staged is not null && !IsBusy && !IsInstalling && !_runState.IsGameRunning;

    /// <summary>Once, after the main window shows: offers a package staged earlier, else checks if enabled. Failures are only logged (by Core).</summary>
    public async Task StartAsync(CancellationToken ct = default)
    {
        if (_started || _updates.IsDevelopmentBuild)
        {
            return;
        }

        _started = true;

        await _updates.CleanUpLeftoversAsync(ct);
        if (await _updates.GetStagedAsync(ct) is { } staged)
        {
            ShowStaged(staged);
            return;
        }

        if (AutoCheck)
        {
            await RunAsync(userRequested: false, ct);
        }
    }

    /// <summary>Errors show in <see cref="Error"/>.</summary>
    public Task CheckNowAsync(CancellationToken ct = default) => RunAsync(userRequested: true, ct);

    /// <summary>Installs the staged package, starts the new version and closes this one; on failure keeps running and sets <see cref="RestartError"/>.</summary>
    public async Task RestartAsync()
    {
        if (!CanRestart || Staged is not { } staged)
        {
            return;
        }

        RestartError = null;
        IsInstalling = true;
        try
        {
            var installed = await _updates.InstallAsync(staged);
            if (!installed.IsSuccess)
            {
                RestartError = installed.Error;
                return;
            }

            Staged = null;
            InstalledVersion = staged.Version;
            Status = UpdateStatus.Installed;

            var started = await _updates.StartInstalledAsync();
            if (!started.IsSuccess)
            {
                RestartError = started.Error;
                return;
            }

            _app.Close();
        }
        finally
        {
            IsInstalling = false;
        }
    }

    /// <summary>For the close-time flush: a failure is logged by Core and the package is offered again next start.</summary>
    public async Task InstallPendingOnCloseAsync(CancellationToken ct)
    {
        if (Staged is not { } staged || IsInstalling)
        {
            return;
        }

        IsInstalling = true;
        try
        {
            if ((await _updates.InstallAsync(staged, ct)).IsSuccess)
            {
                Staged = null;
            }
        }
        finally
        {
            IsInstalling = false;
        }
    }

    partial void OnSourceChanged(UpdateSource value)
    {
        if (_loaded)
        {
            // Never throws for I/O; a failed save keeps the choice for this session.
            _ = _preferences.SaveUpdateSourceAsync(value);
        }
    }

    partial void OnAutoCheckChanged(bool value)
    {
        if (_loaded)
        {
            _ = _preferences.SaveAutoCheckUpdatesAsync(value);
        }
    }

    private async Task RunAsync(bool userRequested, CancellationToken ct)
    {
        if (!CanCheck)
        {
            return;
        }

        Error = null;
        Status = UpdateStatus.Checking;
        var check = await _updates.CheckAsync(Source, ct);
        if (!check.IsSuccess)
        {
            Fail(check.Error, userRequested);
            return;
        }

        var release = check.Value.Available;
        Release = release;
        Available = null;
        if (Staged is { } staged && (release is null || release.Version <= staged.Version))
        {
            ShowStaged(staged);
            return;
        }

        if (release is null)
        {
            Status = UpdateStatus.UpToDate;
            return;
        }

        if (!_updates.CanInstallInPlace)
        {
            Available = release;
            Status = UpdateStatus.Available;
            return;
        }

        DownloadPercent = null;
        Status = UpdateStatus.Downloading;
        var progress = new ContextProgress<DownloadProgress>(report =>
            DownloadPercent = ProgressPercent.Of(report.Received, report.Total ?? release.Size));
        var downloaded = await _updates.DownloadAsync(release, progress, ct);
        if (!downloaded.IsSuccess)
        {
            Fail(downloaded.Error, userRequested);
            return;
        }

        ShowStaged(downloaded.Value);
    }

    private void ShowStaged(StagedUpdate staged)
    {
        Staged = staged;
        Status = UpdateStatus.Ready;
    }

    private void Fail(Error error, bool userRequested)
    {
        if (!userRequested)
        {
            Status = Staged is null ? UpdateStatus.Idle : UpdateStatus.Ready;
            return;
        }

        Error = error;
        Status = UpdateStatus.Failed;
    }

    private void OnRunStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GameRunState.IsGameRunning))
        {
            OnPropertyChanged(nameof(CanRestart));
        }
    }
}
