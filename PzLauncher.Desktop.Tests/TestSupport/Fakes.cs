using PzLauncher.Core.Common;
using PzLauncher.Core.Installs;
using PzLauncher.Core.Java;
using PzLauncher.Core.Launch;
using PzLauncher.Core.Platform;
using PzLauncher.Desktop.Localization;
using PzLauncher.Desktop.Services;
using PzLauncher.Desktop.Themes;
using PzLauncher.Desktop.ViewModels.Java;

namespace PzLauncher.Desktop.Tests.TestSupport;

/// <summary>Set <see cref="ScanGate"/> to hold a scan open and observe the busy state.</summary>
public sealed class FakeInstallDetectionService : IInstallDetectionService
{
    public Queue<Result<InstallScanResult>> ScanResults { get; } = new();

    public TaskCompletionSource? ScanGate { get; set; }

    public Func<string, Result<PzInstall>> Inspect { get; set; } =
        path => new PzInstall(PzInstallKind.Game, path, Steam: null);

    public int ScanCalls { get; private set; }

    public List<string> InspectedPaths { get; } = [];

    public async Task<Result<InstallScanResult>> ScanAsync(CancellationToken ct = default)
    {
        ScanCalls++;
        if (ScanGate is { } gate)
        {
            await gate.Task.WaitAsync(ct);
        }

        ct.ThrowIfCancellationRequested();
        return ScanResults.Count > 0 ? ScanResults.Dequeue() : new InstallScanResult([], []);
    }

    public Task<Result<PzInstall>> InspectAsync(string path, CancellationToken ct = default)
    {
        InspectedPaths.Add(path);
        return Task.FromResult(Inspect(path));
    }

    public static InstallScanResult Found(params PzInstall[] installs) => new(installs, []);
}

/// <summary>Set <see cref="ScanGate"/> to hold a scan open and observe the busy state.</summary>
public sealed class FakeJavaDiscoveryService : IJavaDiscoveryService
{
    public Queue<Result<JavaScanResult>> ScanResults { get; } = new();

    public TaskCompletionSource? ScanGate { get; set; }

    /// <summary>Default: the folder is a 64-bit Java home with an unknown version.</summary>
    public Func<string, Result<JavaRuntime>> Inspect { get; set; } = path => TestJava.Manual(path);

    public int ScanCalls { get; private set; }

    public List<string> InspectedPaths { get; } = [];

    public async Task<Result<JavaScanResult>> ScanAsync(CancellationToken ct = default)
    {
        ScanCalls++;
        if (ScanGate is { } gate)
        {
            await gate.Task.WaitAsync(ct);
        }

        ct.ThrowIfCancellationRequested();
        return ScanResults.Count > 0 ? ScanResults.Dequeue() : new JavaScanResult([], []);
    }

    public Task<Result<JavaRuntime>> InspectAsync(string path, CancellationToken ct = default)
    {
        InspectedPaths.Add(path);
        return Task.FromResult(Inspect(path));
    }

    public static JavaScanResult Found(params JavaRuntime[] runtimes) => new(runtimes, []);
}

public static class TestJava
{
    public static readonly JavaRuntime Jdk25 = Discovered(@"C:\Program Files\Eclipse Adoptium\jdk-25", 25, JavaRuntimeSource.Registry);

    public static readonly JavaRuntime Jdk21 = Discovered(@"C:\Program Files\Eclipse Adoptium\jdk-21", 21, JavaRuntimeSource.JavaHome);

    public static JavaRuntimesViewModel ViewModel(ILocalizationService localization, FakeJavaDiscoveryService discovery, IPreferenceStore store) =>
        new(discovery, new FakeFolderPicker(), store, new UserPreferences(null, null, null), new FakePlatformEnvironment(), localization);

    public static JavaRuntime Manual(string home) =>
        new(home, home + @"\bin\javaw.exe", null, null, JavaRuntimeSource.Manual, HasGraalCompiler: false);

    private static JavaRuntime Discovered(string home, int major, JavaRuntimeSource source) =>
        new(home, home + @"\bin\javaw.exe", new JavaVersion(major, $"{major}.0.1"), "Eclipse Adoptium", source, HasGraalCompiler: false);
}

public static class TestInstalls
{
    public static readonly PzInstall SteamGame =
        new(PzInstallKind.Game, @"E:\SteamLibrary\steamapps\common\ProjectZomboid", new SteamAppInfo("100", null));

    public static readonly PzInstall ManualGame = new(PzInstallKind.Game, @"D:\Games\ProjectZomboid", Steam: null);

    public static readonly PzInstall SteamServer =
        new(PzInstallKind.DedicatedServer, @"E:\SteamLibrary\steamapps\common\Project Zomboid Dedicated Server", new SteamAppInfo("200", "unstable"));
}

public static class TestErrors
{
    // ViewModels map errors by Code and Details only, so Kind is arbitrary here.
    public static Error Create(ErrorCode code, string? path = null) =>
        new(code, "developer text", ErrorKind.Unexpected)
        {
            Details = path is null ? new Dictionary<string, string>() : new Dictionary<string, string> { ["path"] = path },
        };
}

public sealed class FakeFolderPicker : IFolderPicker
{
    public FolderPickResult Next { get; set; } = new FolderPickResult.Cancelled();

    public void PickNext(string path) => Next = new FolderPickResult.Picked(path);

    public Task<FolderPickResult> PickFolderAsync(string title, CancellationToken ct = default) => Task.FromResult(Next);
}

public sealed class FakePreferenceStore : IPreferenceStore
{
    public List<AppLanguage> SavedLanguages { get; } = [];

    public List<AppTheme> SavedThemes { get; } = [];

    public List<IReadOnlyList<string>> SavedManualJavaHomes { get; } = [];

    public List<(string Root, GameLaunchOverrides Overrides)> SavedInstallOverrides { get; } = [];

    public UserPreferences Load() => new(null, null, null);

    public Task SaveLanguageAsync(AppLanguage language, CancellationToken ct = default)
    {
        SavedLanguages.Add(language);
        return Task.CompletedTask;
    }

    public Task SaveThemeAsync(AppTheme theme, CancellationToken ct = default)
    {
        SavedThemes.Add(theme);
        return Task.CompletedTask;
    }

    public Task SaveWindowPlacementAsync(WindowPlacement placement, CancellationToken ct = default) => Task.CompletedTask;

    public Task SaveManualJavaHomesAsync(IReadOnlyList<string> homes, CancellationToken ct = default)
    {
        SavedManualJavaHomes.Add(homes);
        return Task.CompletedTask;
    }

    /// <summary>Holds install override saves open until completed.</summary>
    public TaskCompletionSource? InstallOverridesSaveGate { get; set; }

    public Task SaveInstallOverridesAsync(string installRoot, GameLaunchOverrides overrides, CancellationToken ct = default)
    {
        SavedInstallOverrides.Add((installRoot, overrides));
        return InstallOverridesSaveGate?.Task ?? Task.CompletedTask;
    }
}

/// <summary>Each successful launch starts a game that runs until <see cref="Exit"/> completes.</summary>
public sealed class FakeGameLaunchService : IGameLaunchService
{
    public Func<string, Result<GameLaunchDefaults>> Defaults { get; set; } = _ => TestLaunch.Defaults();

    public Func<LaunchGameRequest, Result<GameSession>> Launch { get; set; } = request => new GameSession(request.InstallRoot, 4242);

    public Func<LaunchGameRequest, Task<Result>> Validate { get; set; } = _ => Task.FromResult(Result.Ok());

    public List<LaunchGameRequest> Launches { get; } = [];

    public List<LaunchGameRequest> Validations { get; } = [];

    public TaskCompletionSource<Result> Exit { get; private set; } = Exited();

    public Task<Result<GameLaunchDefaults>> GetDefaultsAsync(string installRoot, CancellationToken ct = default) =>
        Task.FromResult(Defaults(installRoot));

    public Task<Result<GameSession>> LaunchAsync(LaunchGameRequest request, CancellationToken ct = default)
    {
        Launches.Add(request);
        var result = Launch(request);
        if (result.IsSuccess)
        {
            Exit = new TaskCompletionSource<Result>();
        }

        return Task.FromResult(result);
    }

    public Task<Result> ValidateAsync(LaunchGameRequest request, CancellationToken ct = default)
    {
        Validations.Add(request);
        return Validate(request);
    }

    public Task<Result> WaitForExitAsync(CancellationToken ct = default) => Exit.Task.WaitAsync(ct);

    private static TaskCompletionSource<Result> Exited()
    {
        var exited = new TaskCompletionSource<Result>();
        exited.SetResult(Result.Ok());
        return exited;
    }
}

public static class TestLaunch
{
    private static readonly string BundledHome = TestInstalls.SteamGame.RootPath + @"\jre64";

    public static readonly JavaRuntime BundledJava =
        new(BundledHome, BundledHome + @"\bin\javaw.exe", new JavaVersion(25, "25.0.1"), "Azul Systems, Inc.", JavaRuntimeSource.Manual, HasGraalCompiler: false);

    public static GameLaunchDefaults Defaults(int? maxMemoryMb = 3072, int? minMemoryMb = null, params Error[] warnings) =>
        new(maxMemoryMb, minMemoryMb, ["-Xmx3072m", "-XX:+UseZGC"], BundledJava, 16 * 1024, warnings, GarbageCollector.Z, GarbageCollectorSetByViewpoint: false);
}

public sealed class FakeInstallOverridesService : IInstallOverridesService
{
    private readonly Dictionary<string, GameLaunchOverrides> _overrides = new(StringComparer.OrdinalIgnoreCase);

    public List<(string Root, GameLaunchOverrides Overrides)> Saved { get; } = [];

    public GameLaunchOverrides Get(string installRoot) => _overrides.GetValueOrDefault(installRoot) ?? GameLaunchOverrides.Empty;

    /// <summary>Arranges a saved value without recording a save.</summary>
    public void Set(string installRoot, GameLaunchOverrides overrides) => _overrides[installRoot] = overrides;

    public void Save(string installRoot, GameLaunchOverrides overrides)
    {
        _overrides[installRoot] = overrides;
        Saved.Add((installRoot, overrides));
    }

    public Task FlushAsync(CancellationToken ct = default) => Task.CompletedTask;
}

public sealed class FakeDialogService : IDialogService
{
    public List<string> Errors { get; } = [];

    public event Action? Shown;

    public Task ShowErrorAsync(string message)
    {
        Errors.Add(message);
        Shown?.Invoke();
        return Task.CompletedTask;
    }
}

/// <summary>Time stands still until <see cref="Advance"/>; due one-shot timers then fire on the caller's thread.</summary>
public sealed class ManualTimeProvider : TimeProvider
{
    private readonly List<ManualTimer> _timers = [];
    private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _now;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        timer.Change(dueTime, period);
        _timers.Add(timer);
        return timer;
    }

    public void Advance(TimeSpan by)
    {
        _now += by;
        foreach (var timer in _timers.ToList())
        {
            timer.FireIfDue();
        }
    }

    private sealed class ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state) : ITimer
    {
        private DateTimeOffset? _due;

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            _due = dueTime == Timeout.InfiniteTimeSpan ? null : owner._now + dueTime;
            return true;
        }

        public void FireIfDue()
        {
            if (_due <= owner._now)
            {
                _due = null;
                callback(state);
            }
        }

        public void Dispose() => _due = null;

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}

public sealed class FakeThemeService(AppTheme initial) : IThemeService
{
    public AppTheme Current { get; private set; } = initial;

    public List<AppTheme> Applied { get; } = [];

    public void SetTheme(AppTheme theme)
    {
        Current = theme;
        Applied.Add(theme);
    }
}

public sealed class FakeWindowPlacementService : IWindowPlacementService
{
    public int ResetCalls { get; private set; }

    public void ResetToDefault() => ResetCalls++;
}

public sealed class FakePlatformEnvironment : IPlatformEnvironment
{
    public PlatformKind Os { get; set; } = PlatformKind.Windows;

    public string HomeDirectory { get; set; } = @"C:\Users\Test";

    public Version OsVersion { get; set; } = new(10, 0, 26100, 0);

    public long TotalMemoryBytes { get; set; } = 16L * 1024 * 1024 * 1024;

    public StringComparison PathComparison { get; set; } = StringComparison.OrdinalIgnoreCase;

    public string? GetEnvironmentVariable(string name) => null;

    public string? ReadSteamPathFromRegistry() => null;

    public IReadOnlyList<string> GetRegistrySubKeyNames(RegistryHiveKind hive, string path) => [];

    public string? ReadRegistryString(RegistryHiveKind hive, string path, string name) => null;
}
