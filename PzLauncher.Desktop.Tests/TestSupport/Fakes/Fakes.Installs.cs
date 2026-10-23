using PzLauncher.Core.Common;
using PzLauncher.Core.Installs;
using PzLauncher.Desktop.Services.Shell.Dialogs;
using PzLauncher.Desktop.Services.Shell;

namespace PzLauncher.Desktop.Tests.TestSupport.Fakes;

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

public static class TestInstalls
{
    public static readonly PzInstall SteamGame =
        new(PzInstallKind.Game, @"E:\SteamLibrary\steamapps\common\ProjectZomboid", new SteamAppInfo("100", null));

    public static readonly PzInstall ManualGame = new(PzInstallKind.Game, @"D:\Games\ProjectZomboid", Steam: null);

    public static readonly PzInstall SteamServer =
        new(PzInstallKind.DedicatedServer, @"E:\SteamLibrary\steamapps\common\Project Zomboid Dedicated Server", new SteamAppInfo("200", "unstable"));
}

public sealed class FakeFolderPicker : IFolderPicker
{
    public FolderPickResult Next { get; set; } = new FolderPickResult.Cancelled();

    public void PickNext(string path) => Next = new FolderPickResult.Picked(path);

    public Task<FolderPickResult> PickFolderAsync(string title, CancellationToken ct = default) => Task.FromResult(Next);
}
