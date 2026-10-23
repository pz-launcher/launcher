using PzLauncher.Core.Common;
using PzLauncher.Core.Java;
using PzLauncher.Core.Launch;
using PzLauncher.Desktop.Services.Launch;

namespace PzLauncher.Desktop.Tests.TestSupport.Fakes;

/// <summary>
/// Each successful launch starts a game that runs until <see cref="Exit"/> completes, and outlives its early-exit window once
/// <see cref="PastEarlyExit"/> is set; an exit before that counts as an early one.
/// </summary>
public sealed class FakeGameLaunchService : IGameLaunchService
{
    public Func<string, Result<GameLaunchDefaults>> Defaults { get; set; } = _ => TestLaunch.Defaults();

    public Func<LaunchGameRequest, Result<GameSession>> Launch { get; set; } = request => new GameSession(request.InstallRoot, 4242);

    public Func<LaunchGameRequest, Task<Result>> Validate { get; set; } = _ => Task.FromResult(Result.Ok());

    public List<LaunchGameRequest> Launches { get; } = [];

    public List<LaunchGameRequest> Validations { get; } = [];

    public List<string> DefaultsRequests { get; } = [];

    public TaskCompletionSource<Result> Exit { get; private set; } = Exited();

    public TaskCompletionSource<bool> PastEarlyExit { get; private set; } = Decided(false);

    /// <summary>Sets <see cref="PastEarlyExit"/> as each game starts.</summary>
    public bool OutlivesEarlyExit { get; set; }

    public Task<Result<GameLaunchDefaults>> GetDefaultsAsync(string installRoot, CancellationToken ct = default)
    {
        DefaultsRequests.Add(installRoot);
        return Task.FromResult(Defaults(installRoot));
    }

    public Task<Result<GameSession>> LaunchAsync(LaunchGameRequest request, CancellationToken ct = default)
    {
        Launches.Add(request);
        var result = Launch(request);
        if (result.IsSuccess)
        {
            var pastEarlyExit = new TaskCompletionSource<bool>();
            Exit = new TaskCompletionSource<Result>();
            Exit.Task.ContinueWith(_ => pastEarlyExit.TrySetResult(false), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            if (OutlivesEarlyExit)
            {
                pastEarlyExit.SetResult(true);
            }

            PastEarlyExit = pastEarlyExit;
        }

        return Task.FromResult(result);
    }

    public Task<Result> ValidateAsync(LaunchGameRequest request, CancellationToken ct = default)
    {
        Validations.Add(request);
        return Validate(request);
    }

    public Task<Result> WaitForExitAsync(CancellationToken ct = default) => Exit.Task.WaitAsync(ct);

    public Task<bool> WaitPastEarlyExitAsync(CancellationToken ct = default) => PastEarlyExit.Task.WaitAsync(ct);

    private static TaskCompletionSource<Result> Exited()
    {
        var exited = new TaskCompletionSource<Result>();
        exited.SetResult(Result.Ok());
        return exited;
    }

    private static TaskCompletionSource<bool> Decided(bool pastEarlyExit)
    {
        var decided = new TaskCompletionSource<bool>();
        decided.SetResult(pastEarlyExit);
        return decided;
    }
}

public static class TestLaunch
{
    private static readonly string BundledHome = TestInstalls.SteamGame.RootPath + @"\jre64";

    public static readonly JavaRuntime BundledJava =
        new(BundledHome, BundledHome + @"\bin\javaw.exe", new JavaVersion(25, "25.0.1"), "Azul Systems, Inc.", JavaRuntimeSource.Manual, HasGraalCompiler: false, HasInstrumentLibrary: true);

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
