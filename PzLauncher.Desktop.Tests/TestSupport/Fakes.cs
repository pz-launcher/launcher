using PzLauncher.Core.Common;
using PzLauncher.Core.Installs;
using PzLauncher.Core.Java;
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
        new(home, home + @"\bin\javaw.exe", null, null, JavaRuntimeSource.Manual);

    private static JavaRuntime Discovered(string home, int major, JavaRuntimeSource source) =>
        new(home, home + @"\bin\javaw.exe", new JavaVersion(major, $"{major}.0.1"), "Eclipse Adoptium", source);
}

public static class TestInstalls
{
    public static readonly PzInstall SteamGame =
        new(PzInstallKind.Game, @"E:\SteamLibrary\steamapps\common\ProjectZomboid", new SteamAppInfo("100", null));

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

    public StringComparison PathComparison { get; set; } = StringComparison.OrdinalIgnoreCase;

    public string? GetEnvironmentVariable(string name) => null;

    public string? ReadSteamPathFromRegistry() => null;

    public IReadOnlyList<string> GetRegistrySubKeyNames(RegistryHiveKind hive, string path) => [];

    public string? ReadRegistryString(RegistryHiveKind hive, string path, string name) => null;
}
