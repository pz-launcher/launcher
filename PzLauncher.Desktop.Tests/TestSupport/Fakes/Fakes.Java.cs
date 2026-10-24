using PzLauncher.Core.Common;
using PzLauncher.Core.Java;
using PzLauncher.Desktop.Localization;
using PzLauncher.Desktop.Services.Preferences;
using PzLauncher.Desktop.ViewModels.Java;

namespace PzLauncher.Desktop.Tests.TestSupport.Fakes;

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
        new(home, home + @"\bin\javaw.exe", null, null, JavaRuntimeSource.Manual, HasGraalCompiler: false, HasInstrumentLibrary: true);

    private static JavaRuntime Discovered(string home, int major, JavaRuntimeSource source) =>
        new(home, home + @"\bin\javaw.exe", new JavaVersion(major, $"{major}.0.1"), "Eclipse Adoptium", source, HasGraalCompiler: false, HasInstrumentLibrary: true);
}
