using System.ComponentModel;
using System.Globalization;
using Microsoft.Extensions.Logging;
using PzLauncher.Core.Common;
using PzLauncher.Core.Installs;
using PzLauncher.Core.Java;
using PzLauncher.Core.Platform;

namespace PzLauncher.Core.Launch;

internal sealed partial class GameLaunchService(
    IInstallDetectionService installs,
    IJavaDiscoveryService javaDiscovery,
    IPlatformEnvironment platform,
    IProcessRunner processes,
    TimeProvider time,
    ILogger<GameLaunchService> logger) : IGameLaunchService
{
    private const long BytesPerMb = 1024 * 1024;

    // A failed JVM start (bad option, missing GC) exits within a few seconds; later exits are the game's own.
    private static readonly TimeSpan EarlyExitWindow = TimeSpan.FromSeconds(30);

    private readonly SemaphoreSlim _launchGate = new(1, 1);

    // Completes once the tracked game has exited and its process handle is released.
    private volatile Task<Result> _exit = Task.FromResult(Result.Ok());

    public async Task<Result<GameLaunchDefaults>> GetDefaultsAsync(string installRoot, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var install = await InspectGameAsync(installRoot, ct).ConfigureAwait(false);
        if (!install.IsSuccess)
        {
            return install.Error;
        }

        var game = ResolveGameFolder(install.Value.RootPath);
        var bundled = await javaDiscovery.InspectAsync(Path.Combine(game, PzLaunchFiles.BundledJreFolder), ct).ConfigureAwait(false);
        var bundledJava = bundled.IsSuccess ? bundled.Value : null;
        var systemMemoryMb = (int)Math.Min(platform.TotalMemoryBytes / BytesPerMb, int.MaxValue);

        var config = await ReadConfigAsync(game, ct).ConfigureAwait(false);
        if (!config.IsSuccess)
        {
            return new GameLaunchDefaults(null, null, [], bundledJava, systemMemoryMb, [config.Error], null, false);
        }

        var vmArgs = config.Value.JvmArgs(platform.Os, platform.OsVersion);
        var gc = GarbageCollectors.FromFlag(vmArgs.LastOrDefault(GarbageCollectors.IsFlag));
        return new GameLaunchDefaults(
            LaunchArguments.HeapSizeMb(vmArgs, LaunchArguments.MaxHeapPrefix),
            LaunchArguments.HeapSizeMb(vmArgs, LaunchArguments.InitialHeapPrefix),
            vmArgs,
            bundledJava,
            systemMemoryMb,
            [],
            gc,
            gc == GarbageCollector.Shenandoah && HasViewpointBackup(game));
    }

    public async Task<Result<GameSession>> LaunchAsync(LaunchGameRequest request, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var check = CheckRequest(request.Overrides);
        if (!check.IsSuccess)
        {
            return check.Error;
        }

        await _launchGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!_exit.IsCompleted)
            {
                return LaunchErrors.AlreadyRunning();
            }

            var plan = await PlanAsync(request.InstallRoot, request.Overrides, ct).ConfigureAwait(false);
            return plan.IsSuccess ? Start(plan.Value) : plan.Error;
        }
        finally
        {
            _launchGate.Release();
        }
    }

    public async Task<Result> ValidateAsync(LaunchGameRequest request, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var check = CheckRequest(request.Overrides);
        if (!check.IsSuccess)
        {
            return check;
        }

        var plan = await PlanAsync(request.InstallRoot, request.Overrides, ct).ConfigureAwait(false);
        return plan.IsSuccess ? Result.Ok() : plan.Error;
    }

    public Task<Result> WaitForExitAsync(CancellationToken ct = default) => _exit.WaitAsync(ct);

    private Result CheckRequest(GameLaunchOverrides overrides)
    {
        if (platform.Os == PlatformKind.MacOS)
        {
            return LaunchErrors.PlatformUnsupported(platform.Os);
        }

        if (overrides.MaxMemoryMb is int max and <= 0)
        {
            return LaunchErrors.InvalidMemory(max);
        }

        if (overrides.MinMemoryMb is int min and <= 0)
        {
            return LaunchErrors.InvalidMemory(min);
        }

        return Result.Ok();
    }

    private async Task<Result<LaunchPlan>> PlanAsync(string installRoot, GameLaunchOverrides overrides, CancellationToken ct)
    {
        var install = await InspectGameAsync(installRoot, ct).ConfigureAwait(false);
        if (!install.IsSuccess)
        {
            return install.Error;
        }

        var game = ResolveGameFolder(install.Value.RootPath);
        var config = await ReadConfigAsync(game, ct).ConfigureAwait(false);
        if (!config.IsSuccess)
        {
            return config.Error;
        }

        string javaHome;
        if (overrides.JavaHome is null)
        {
            javaHome = Path.Combine(game, PzLaunchFiles.BundledJreFolder);
        }
        else if (!PathNormalizer.TryNormalizeAbsolute(overrides.JavaHome, out var normalizedHome))
        {
            return LaunchErrors.JavaMissing(overrides.JavaHome);
        }
        else
        {
            javaHome = normalizedHome;
        }

        var executable = PzLaunchFiles.JavaExecutable(javaHome, platform.Os);
        if (!File.Exists(executable))
        {
            return LaunchErrors.JavaMissing(executable);
        }

        var jsonJvmArgs = config.Value.JvmArgs(platform.Os, platform.OsVersion);
        var maxMb = overrides.MaxMemoryMb ?? LaunchArguments.HeapSizeMb(jsonJvmArgs, LaunchArguments.MaxHeapPrefix);
        var minMb = overrides.MinMemoryMb ?? LaunchArguments.HeapSizeMb(jsonJvmArgs, LaunchArguments.InitialHeapPrefix);
        if (minMb is int min && maxMb is int max && min > max)
        {
            return LaunchErrors.InvalidMemory(min);
        }

        var extraJvmArgs = LaunchArguments.Split(overrides.ExtraJvmArgs);
        var gc = GarbageCollectors.Choose(overrides.GarbageCollector, extraJvmArgs, jsonJvmArgs);
        if (!gc.IsSuccess)
        {
            return gc.Error;
        }

        var jvmArgs = LaunchArguments.JvmArgs(jsonJvmArgs, overrides, extraJvmArgs);
        var gcCheck = await CheckGraalShenandoahAsync(gc.Value, jvmArgs, javaHome, game, ct).ConfigureAwait(false);
        if (!gcCheck.IsSuccess)
        {
            return gcCheck.Error;
        }

        var start = new ProcessStartRequest(
            executable,
            LaunchArguments.Build(config.Value, platform.Os, jvmArgs, overrides),
            game,
            BuildEnvironment(game, javaHome));
        return new LaunchPlan(install.Value.RootPath, game, start, jvmArgs);
    }

    // The Graal JIT rejects Shenandoah at JVM start (task research/gc-graal-viewpoint.md); other GCs work with it.
    private async Task<Result> CheckGraalShenandoahAsync(
        GcChoice gc, IReadOnlyList<string> jvmArgs, string javaHome, string game, CancellationToken ct)
    {
        if (!string.Equals(gc.Flag, GarbageCollectors.Flag(GarbageCollector.Shenandoah), StringComparison.Ordinal)
            || GarbageCollectors.DisablesJvmciCompiler(jvmArgs))
        {
            return Result.Ok();
        }

        var java = await javaDiscovery.InspectAsync(javaHome, ct).ConfigureAwait(false);
        if (!java.IsSuccess || !java.Value.HasGraalCompiler)
        {
            return Result.Ok();
        }

        return gc.FromJson && HasViewpointBackup(game)
            ? LaunchErrors.GcIncompatibleViewpoint(javaHome)
            : LaunchErrors.GcIncompatible(javaHome);
    }

    private Result<GameSession> Start(LaunchPlan plan)
    {
        var startedAt = time.GetUtcNow();
        IRunningProcess process;
        try
        {
            process = processes.Start(plan.Start);
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException)
        {
            var error = LaunchErrors.StartFailed(plan.Start.FileName, ex.Message);
            LogWarning(logger, error.Code.ToWireName(), error.Message);
            return error;
        }

        var session = new GameSession(plan.InstallRoot, process.Id);
        _exit = TrackAsync(process, plan, startedAt);
        return session;
    }

    private async Task<Result> TrackAsync(IRunningProcess process, LaunchPlan plan, DateTimeOffset startedAt)
    {
        using (process)
        {
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            var exit = ExitResult(process, plan, startedAt);
            if (!exit.IsSuccess)
            {
                LogWarning(logger, exit.Error.Code.ToWireName(), exit.Error.Message);
            }

            return exit;
        }
    }

    private Result ExitResult(IRunningProcess process, LaunchPlan plan, DateTimeOffset startedAt)
    {
        if (FindCrashLog(plan, process.Id, startedAt) is { } crashLog)
        {
            return LaunchErrors.GameCrashed(crashLog, process.ExitCode);
        }

        if (process.ExitCode != 0 && time.GetUtcNow() - startedAt < EarlyExitWindow)
        {
            return LaunchErrors.GameExitedEarly(process.ExitCode, string.Join('\n', process.ErrorOutputTail));
        }

        return Result.Ok();
    }

    // A log left by an earlier process with the same id is older than this launch.
    private static string? FindCrashLog(LaunchPlan plan, int processId, DateTimeOffset startedAt)
    {
        var candidates = new List<string>();
        if (plan.JvmArgs.LastOrDefault(arg => arg.StartsWith(PzLaunchFiles.ErrorFileOption, StringComparison.Ordinal)) is { } option)
        {
            var path = option[PzLaunchFiles.ErrorFileOption.Length..]
                .Replace("%p", processId.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
            if (path.Length > 0 && !PathNormalizer.HasInvalidPathChars(path))
            {
                candidates.Add(Path.GetFullPath(path, plan.GameFolder));
            }
        }

        // The JVM falls back to its working directory when it can't write the ErrorFile.
        candidates.Add(Path.Combine(plan.GameFolder, PzLaunchFiles.DefaultCrashLog(processId)));
        return candidates.FirstOrDefault(path =>
            new FileInfo(path) is { Exists: true } log && new DateTimeOffset(log.LastWriteTimeUtc) >= startedAt);
    }

    private async Task<Result<PzInstall>> InspectGameAsync(string installRoot, CancellationToken ct)
    {
        var install = await installs.InspectAsync(installRoot, ct).ConfigureAwait(false);
        if (!install.IsSuccess)
        {
            return install.Error;
        }

        return install.Value.Kind switch
        {
            PzInstallKind.Game => install.Value,
            PzInstallKind.DedicatedServer => LaunchErrors.NotAGame(install.Value.RootPath),
        };
    }

    // The Linux Steam depot nests the game one level down; when neither has the json, the root is named in the error.
    private static string ResolveGameFolder(string root)
    {
        if (File.Exists(Path.Combine(root, PzLaunchFiles.ConfigFile)))
        {
            return root;
        }

        var nested = Path.Combine(root, PzLaunchFiles.NestedGameFolder);
        return File.Exists(Path.Combine(nested, PzLaunchFiles.ConfigFile)) ? nested : root;
    }

    private static bool HasViewpointBackup(string game) => File.Exists(Path.Combine(game, PzLaunchFiles.ViewpointBackupFile));

    private async Task<Result<PzLaunchConfig>> ReadConfigAsync(string game, CancellationToken ct)
    {
        var config = await PzLaunchConfig.ReadAsync(Path.Combine(game, PzLaunchFiles.ConfigFile), ct).ConfigureAwait(false);
        if (!config.IsSuccess)
        {
            LogWarning(logger, config.Error.Code.ToWireName(), config.Error.Message);
        }

        return config;
    }

    private Dictionary<string, string> BuildEnvironment(string game, string javaHome) => platform.Os switch
    {
        PlatformKind.Linux => LinuxLaunchEnvironment.Build(
            game,
            javaHome,
            platform.GetEnvironmentVariable(LinuxLaunchEnvironment.LibraryPathVariable),
            platform.GetEnvironmentVariable(LinuxLaunchEnvironment.PreloadVariable)),
        PlatformKind.Windows or PlatformKind.MacOS => [],
    };

    [LoggerMessage(Level = LogLevel.Warning, Message = "Game launch: {Code}: {Detail}")]
    private static partial void LogWarning(ILogger logger, string code, string detail);

    /// <param name="JvmArgs">The effective JVM args, read again when the game exits (<c>-XX:ErrorFile</c>).</param>
    private sealed record LaunchPlan(string InstallRoot, string GameFolder, ProcessStartRequest Start, IReadOnlyList<string> JvmArgs);
}
