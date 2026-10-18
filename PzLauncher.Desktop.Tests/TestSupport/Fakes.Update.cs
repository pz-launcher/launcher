using PzLauncher.Core.Common;
using PzLauncher.Core.Update;
using PzLauncher.Desktop.Services.Launch;
using PzLauncher.Desktop.Services.Preferences;
using PzLauncher.Desktop.Services.Shell;
using PzLauncher.Desktop.Services.Update;

namespace PzLauncher.Desktop.Tests.TestSupport;

/// <summary>Records every call; <see cref="Checks"/> and <see cref="Downloads"/> stand for network requests.</summary>
public sealed class FakeUpdateService : IUpdateService
{
    public SemVersion CurrentVersion { get; set; } = TestUpdates.Current;

    public bool IsDevelopmentBuild => CurrentVersion.IsDevelopment;

    public bool CanInstallInPlace { get; set; } = true;

    /// <summary>Default: up to date, or <c>update.development_build</c> on a development build.</summary>
    public Func<UpdateSource, Result<UpdateCheck>>? Check { get; set; }

    public Func<ReleaseInfo, Result<StagedUpdate>> Download { get; set; } = release => TestUpdates.Staged(release.Version);

    /// <summary>Reported in order before <see cref="DownloadGate"/> is awaited.</summary>
    public IReadOnlyList<DownloadProgress> DownloadReports { get; set; } = [];

    public TaskCompletionSource? DownloadGate { get; set; }

    public StagedUpdate? StagedAtStart { get; set; }

    public Func<StagedUpdate, Result> Install { get; set; } = _ => Result.Ok();

    public Func<Result> StartInstalled { get; set; } = Result.Ok;

    public List<UpdateSource> Checks { get; } = [];

    public List<ReleaseInfo> Downloads { get; } = [];

    public List<StagedUpdate> Installs { get; } = [];

    public int StartCalls { get; private set; }

    public int GetStagedCalls { get; private set; }

    public int CleanUpCalls { get; private set; }

    public int RequestCount => Checks.Count + Downloads.Count;

    public Task<Result<UpdateCheck>> CheckAsync(UpdateSource source, CancellationToken ct = default)
    {
        Checks.Add(source);
        Result<UpdateCheck> result = Check is { } check ? check(source)
            : IsDevelopmentBuild ? TestErrors.Create(ErrorCode.UpdateDevelopmentBuild)
            : new UpdateCheck(CurrentVersion, null);
        return Task.FromResult(result);
    }

    public async Task<Result<StagedUpdate>> DownloadAsync(
        ReleaseInfo release, IProgress<DownloadProgress>? progress = null, CancellationToken ct = default)
    {
        Downloads.Add(release);
        foreach (var report in DownloadReports)
        {
            progress?.Report(report);
        }

        if (DownloadGate is { } gate)
        {
            await gate.Task.WaitAsync(ct);
        }

        return Download(release);
    }

    public Task<StagedUpdate?> GetStagedAsync(CancellationToken ct = default)
    {
        GetStagedCalls++;
        return Task.FromResult(StagedAtStart);
    }

    public Task<Result> InstallAsync(StagedUpdate staged, CancellationToken ct = default)
    {
        Installs.Add(staged);
        return Task.FromResult(Install(staged));
    }

    public Task<Result> StartInstalledAsync(CancellationToken ct = default)
    {
        StartCalls++;
        return Task.FromResult(StartInstalled());
    }

    public Task CleanUpLeftoversAsync(CancellationToken ct = default)
    {
        CleanUpCalls++;
        return Task.CompletedTask;
    }
}

public static class TestUpdates
{
    public static readonly SemVersion Current = Version("0.2.0");

    public static SemVersion Version(string text) =>
        SemVersion.TryParse(text, out var version) ? version : throw new ArgumentException($"Not a version: {text}", nameof(text));

    public static ReleaseInfo Release(string version = "0.3.0", UpdateSource source = UpdateSource.GitHub, long? size = 1000) =>
        new(
            Version(version),
            source,
            $"PzLauncher-{version}-win-x64.zip",
            new Uri($"https://example.test/download/PzLauncher-{version}-win-x64.zip"),
            size,
            null,
            new Uri($"https://example.test/releases/v{version}"));

    public static StagedUpdate Staged(SemVersion version) =>
        new(version, $@"C:\Games\PzLauncher\.update\staged\{version}", new Uri($"https://example.test/releases/v{version}"));

    public static Result<UpdateCheck> Found(string version = "0.3.0", UpdateSource source = UpdateSource.GitHub) =>
        new UpdateCheck(Current, Release(version, source));

    public static UpdateCoordinator Coordinator(
        FakeUpdateService updates,
        IPreferenceStore? preferences = null,
        UserPreferences? saved = null,
        GameRunState? runState = null,
        IAppLifetime? app = null) =>
        new(updates, preferences ?? new FakePreferenceStore(), saved ?? new UserPreferences(null, null, null), runState ?? new GameRunState(), app ?? new FakeAppLifetime());
}
