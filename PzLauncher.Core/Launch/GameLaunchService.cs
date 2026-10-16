using System.ComponentModel;
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
    ILogger<GameLaunchService> logger) : IGameLaunchService
{
    private const long BytesPerMb = 1024 * 1024;

    private readonly SemaphoreSlim _launchGate = new(1, 1);

    // Completes once the tracked game has exited and its process handle is released.
    private volatile Task _exit = Task.CompletedTask;

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
            return new GameLaunchDefaults(null, null, [], bundledJava, systemMemoryMb, [config.Error]);
        }

        var vmArgs = config.Value.JvmArgs(platform.Os, platform.OsVersion);
        return new GameLaunchDefaults(
            LaunchArguments.HeapSizeMb(vmArgs, LaunchArguments.MaxHeapPrefix),
            LaunchArguments.HeapSizeMb(vmArgs, LaunchArguments.InitialHeapPrefix),
            vmArgs,
            bundledJava,
            systemMemoryMb,
            []);
    }

    public async Task<Result<GameSession>> LaunchAsync(LaunchGameRequest request, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (platform.Os == PlatformKind.MacOS)
        {
            return LaunchErrors.PlatformUnsupported(platform.Os);
        }

        if (request.Overrides.MaxMemoryMb is int max and <= 0)
        {
            return LaunchErrors.InvalidMemory(max);
        }

        if (request.Overrides.MinMemoryMb is int min and <= 0)
        {
            return LaunchErrors.InvalidMemory(min);
        }

        await _launchGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await StartGameAsync(request.InstallRoot, request.Overrides, ct).ConfigureAwait(false);
        }
        finally
        {
            _launchGate.Release();
        }
    }

    public Task WaitForExitAsync(CancellationToken ct = default) => _exit.WaitAsync(ct);

    private async Task<Result<GameSession>> StartGameAsync(string installRoot, GameLaunchOverrides overrides, CancellationToken ct)
    {
        if (!_exit.IsCompleted)
        {
            return LaunchErrors.AlreadyRunning();
        }

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

        var jvmArgs = config.Value.JvmArgs(platform.Os, platform.OsVersion);
        var maxMb = overrides.MaxMemoryMb ?? LaunchArguments.HeapSizeMb(jvmArgs, LaunchArguments.MaxHeapPrefix);
        var minMb = overrides.MinMemoryMb ?? LaunchArguments.HeapSizeMb(jvmArgs, LaunchArguments.InitialHeapPrefix);
        if (minMb is int min && maxMb is int max && min > max)
        {
            return LaunchErrors.InvalidMemory(min);
        }

        var start = new ProcessStartRequest(
            executable,
            LaunchArguments.Build(config.Value, platform.Os, platform.OsVersion, overrides),
            game,
            BuildEnvironment(game, javaHome));

        IRunningProcess process;
        try
        {
            process = processes.Start(start);
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException)
        {
            var error = LaunchErrors.StartFailed(executable, ex.Message);
            LogWarning(logger, error.Code.ToWireName(), error.Message);
            return error;
        }

        var session = new GameSession(install.Value.RootPath, process.Id);
        _exit = TrackAsync(process);
        return session;
    }

    private static async Task TrackAsync(IRunningProcess process)
    {
        using (process)
        {
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        }
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
}
