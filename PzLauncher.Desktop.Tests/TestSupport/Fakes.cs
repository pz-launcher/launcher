using PzLauncher.Core.Common;
using PzLauncher.Core.Installs;
using PzLauncher.Core.Platform;
using PzLauncher.Desktop.Services;

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

public sealed class FakePlatformEnvironment : IPlatformEnvironment
{
    public PlatformKind Os { get; set; } = PlatformKind.Windows;

    public string HomeDirectory { get; set; } = @"C:\Users\Test";

    public StringComparison PathComparison { get; set; } = StringComparison.OrdinalIgnoreCase;

    public string? GetEnvironmentVariable(string name) => null;

    public string? ReadSteamPathFromRegistry() => null;
}
