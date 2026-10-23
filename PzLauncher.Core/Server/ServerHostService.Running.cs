using System.ComponentModel;
using System.Text;
using System.Text.Json;
using PzLauncher.Core.Common;
using PzLauncher.Core.Java;

namespace PzLauncher.Core.Server;

// The running server: status, stop, kill, commands and console, all from its file in the state folder.
internal sealed partial class ServerHostService
{
    private const int MaxCommandBytes = 4000;
    private const int FailureOutputLines = 30;

    // The admin prompt is followed by a stack trace and shutdown lines.
    private const int FailureScanLines = 200;

    private static readonly TimeSpan ReadyProbeInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ExitPollInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan StartTimeTolerance = TimeSpan.FromSeconds(1);

    // Around every read-modify-write of a running file.
    private readonly SemaphoreSlim _fileGate = new(1, 1);

    private readonly Lock _memoryGate = new();
    private readonly Dictionary<string, SiteMemory> _memory = new(StringComparer.FromComparison(platform.PathComparison));

    // Big worlds take minutes to save.
    internal TimeSpan StopTimeout { get; init; } = TimeSpan.FromMinutes(3);

    internal TimeSpan KillTimeout { get; init; } = TimeSpan.FromSeconds(10);

    public async Task<Result<ServerStatus>> GetStatusAsync(ServerSite site, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return (await ObserveAsync(SitePaths.Of(site), ct).ConfigureAwait(false)).Status;
    }

    public async Task<Result<ServerStatus>> StopAsync(ServerSite site, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var paths = SitePaths.Of(site);
        await _changeGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if ((await ObserveAsync(paths, ct).ConfigureAwait(false)).Alive is not { } running)
            {
                return ServerErrors.NotRunning();
            }

            // Already stopping (an earlier stop timed out): wait again without a second quit.
            if (running.StopRequestedAt is null)
            {
                if (!running.Ready)
                {
                    return ServerErrors.NotReady();
                }

                var quit = await rcon.ExecuteAsync(running.RconPort, running.RconPassword, PzServerFiles.QuitCommand, ct).ConfigureAwait(false);
                if (!quit.IsSuccess)
                {
                    return Failed(quit.Error);
                }

                running = await MarkStoppingAsync(paths, running).ConfigureAwait(false);
            }

            return await WaitForExitAsync(paths, running, StopTimeout, ct).ConfigureAwait(false);
        }
        finally
        {
            _changeGate.Release();
        }
    }

    public async Task<Result<ServerStatus>> KillAsync(ServerSite site, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var paths = SitePaths.Of(site);
        await _changeGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // The identity check in ObserveAsync keeps a reused process id from being killed.
            if ((await ObserveAsync(paths, ct).ConfigureAwait(false)).Alive is not { } running)
            {
                return ServerErrors.NotRunning();
            }

            running = await MarkStoppingAsync(paths, running).ConfigureAwait(false);
            try
            {
                processes.KillTree(running.ProcessId);
            }
            catch (Win32Exception ex)
            {
                // Still alive then: the wait below reports it.
                LogKillFailed(logger, running.ProcessId, ex.Message);
            }

            return await WaitForExitAsync(paths, running, KillTimeout, ct).ConfigureAwait(false);
        }
        finally
        {
            _changeGate.Release();
        }
    }

    public async Task<Result<string>> SendCommandAsync(ServerSite site, string command, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var paths = SitePaths.Of(site);

        // PZ drops packets over 4096 bytes; a line break would end the command early.
        if (string.IsNullOrWhiteSpace(command)
            || command.AsSpan().IndexOfAny('\r', '\n') >= 0
            || Encoding.UTF8.GetByteCount(command) > MaxCommandBytes)
        {
            return ServerErrors.CommandInvalid();
        }

        if ((await ObserveAsync(paths, ct).ConfigureAwait(false)).Alive is not { StopRequestedAt: null } running)
        {
            return ServerErrors.NotRunning();
        }

        if (!running.Ready)
        {
            return ServerErrors.NotReady();
        }

        var response = await rcon.ExecuteAsync(running.RconPort, running.RconPassword, command, ct).ConfigureAwait(false);
        return response.IsSuccess ? response : Failed(response.Error);
    }

    public IAsyncEnumerable<ServerConsoleLine> StreamConsoleAsync(ServerSite site, int backlogLines, CancellationToken ct = default) =>
        ServerConsoleTail.FollowAsync(PzServerFiles.ConsolePath(SitePaths.Of(site).CacheDir), backlogLines, time, ct);

    private async Task<Observation> ObserveAsync(SitePaths paths, CancellationToken ct)
    {
        RunningServer? running;
        await _fileGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            running = await ReadRunningAsync(paths.RunningFile, ct).ConfigureAwait(false);
            if (running is null)
            {
                return new Observation(new ServerStatus(ServerState.Stopped, null, null, null, LastFailure(paths)), null);
            }

            if (!IsAlive(running))
            {
                var failure = await ExitFailureAsync(running, ct).ConfigureAwait(false);
                DeleteRunning(paths.RunningFile);
                RememberFailure(paths, failure);
                if (failure is null)
                {
                    LogStopped(logger, running.Name);
                }
                else
                {
                    Failed(failure);
                }

                return new Observation(new ServerStatus(ServerState.Stopped, null, null, null, failure), null);
            }
        }
        finally
        {
            _fileGate.Release();
        }

        if (running.StopRequestedAt is null && !running.Ready && TryClaimProbe(paths)
            && (await rcon.AuthenticateAsync(running.RconPort, running.RconPassword, ct).ConfigureAwait(false)).IsSuccess)
        {
            running = await MarkReadyAsync(paths, running, ct).ConfigureAwait(false);
        }

        return new Observation(StatusOf(running), running);
    }

    private static ServerStatus StatusOf(RunningServer running) =>
        new(
            running.StopRequestedAt is not null ? ServerState.Stopping : running.Ready ? ServerState.Running : ServerState.Starting,
            running.Name,
            running.ProcessId,
            running.ProcessStartTime,
            null);

    // Name and start time guard against the id being reused by another process, without reading command lines.
    private bool IsAlive(RunningServer running) =>
        processes.GetById(running.ProcessId) is { } process
        && string.Equals(process.Name, Path.GetFileNameWithoutExtension(JavaLocations.ConsoleExecutableName(platform.Os)), StringComparison.OrdinalIgnoreCase)
        && (process.StartTime - running.ProcessStartTime).Duration() <= StartTimeTolerance;

    private async Task<Error?> ExitFailureAsync(RunningServer running, CancellationToken ct)
    {
        if (running.StopRequestedAt is not null)
        {
            return null;
        }

        var lines = await ConsoleOfRunAsync(running, ct).ConfigureAwait(false);
        if (!running.Ready && lines.Any(line => line.Contains(PzServerFiles.AdminPasswordPrompt, StringComparison.Ordinal)))
        {
            return ServerErrors.AdminPasswordRequired(running.Name);
        }

        var output = string.Join('\n', lines.TakeLast(FailureOutputLines));
        return running.Ready ? ServerErrors.ExitedUnexpectedly(running.Name, output) : ServerErrors.ExitedEarly(running.Name, output);
    }

    // A JVM that fails before PZ truncates the file leaves the previous run's lines in it.
    private static async Task<IReadOnlyList<string>> ConsoleOfRunAsync(RunningServer running, CancellationToken ct)
    {
        var path = PzServerFiles.ConsolePath(running.CacheDir);
        return File.GetLastWriteTimeUtc(path) < (running.ProcessStartTime - StartTimeTolerance).UtcDateTime
            ? []
            : await new ServerConsoleTail(path).ReadBacklogAsync(FailureScanLines, ct).ConfigureAwait(false);
    }

    private async Task<Result<ServerStatus>> WaitForExitAsync(SitePaths paths, RunningServer running, TimeSpan timeout, CancellationToken ct)
    {
        // Polled, not waited on: the server isn't this process's child after a host restart.
        var deadline = time.GetUtcNow() + timeout;
        while (IsAlive(running))
        {
            if (time.GetUtcNow() >= deadline)
            {
                return Failed(ServerErrors.StopTimeout(timeout));
            }

            await Task.Delay(ExitPollInterval, time, ct).ConfigureAwait(false);
        }

        await _fileGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (await ReadRunningAsync(paths.RunningFile, CancellationToken.None).ConfigureAwait(false) is { } current && IsSameRun(current, running))
            {
                DeleteRunning(paths.RunningFile);
            }
        }
        finally
        {
            _fileGate.Release();
        }

        RememberFailure(paths, null);
        LogStopped(logger, running.Name);
        return new ServerStatus(ServerState.Stopped, null, null, null, null);
    }

    // Recorded once the request went out, whatever the caller's token says; a failed write only costs the exit's classification.
    private async Task<RunningServer> MarkStoppingAsync(SitePaths paths, RunningServer running)
    {
        var stopping = running with { StopRequestedAt = running.StopRequestedAt ?? time.GetUtcNow() };
        await UpdateRunningAsync(paths, running, stopping, CancellationToken.None).ConfigureAwait(false);
        return stopping;
    }

    private async Task<RunningServer> MarkReadyAsync(SitePaths paths, RunningServer running, CancellationToken ct)
    {
        var ready = running with { Ready = true };
        await UpdateRunningAsync(paths, running, ready, ct).ConfigureAwait(false);
        return ready;
    }

    // Leaves the file alone when another run replaced it, or a status check removed it, in the meantime.
    private async Task UpdateRunningAsync(SitePaths paths, RunningServer expected, RunningServer updated, CancellationToken ct)
    {
        await _fileGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (await ReadRunningAsync(paths.RunningFile, ct).ConfigureAwait(false) is { } current && IsSameRun(current, expected))
            {
                await WriteRunningAsync(paths.RunningFile, updated, ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogRunningFileProblem(logger, paths.RunningFile, ex.Message);
        }
        finally
        {
            _fileGate.Release();
        }
    }

    private static bool IsSameRun(RunningServer a, RunningServer b) =>
        a.ProcessId == b.ProcessId && a.ProcessStartTime == b.ProcessStartTime;

    // Missing means stopped. Unreadable is treated as stopped too, but kept; malformed can never become readable, so it goes.
    private async Task<RunningServer?> ReadRunningAsync(string path, CancellationToken ct)
    {
        var read = await ReadFile.BytesIfExistsAsync(path, reason => ServerErrors.FileAccessFailed(path, reason), ct).ConfigureAwait(false);
        if (read is not { } bytes)
        {
            return null;
        }

        if (!bytes.IsSuccess)
        {
            Failed(bytes.Error);
            return null;
        }

        RunningServer? running = null;
        var problem = "The file holds no JSON object.";
        try
        {
            running = JsonSerializer.Deserialize(bytes.Value, RunningServerJsonContext.Default.RunningServer);
        }
        catch (JsonException ex)
        {
            problem = ex.Message;
        }

        if (running is null)
        {
            LogRunningFileProblem(logger, path, problem);
            DeleteRunning(path);
        }

        return running;
    }

    private static async Task WriteRunningAsync(string path, RunningServer running, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? path);
        await AtomicFile.WriteAsync(path, JsonSerializer.SerializeToUtf8Bytes(running, RunningServerJsonContext.Default.RunningServer), ct)
            .ConfigureAwait(false);
    }

    private void DeleteRunning(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The next status check finds the process gone again and retries.
            LogRunningFileProblem(logger, path, ex.Message);
        }
    }

    private Error? LastFailure(SitePaths paths)
    {
        lock (_memoryGate)
        {
            return _memory.GetValueOrDefault(paths.RunningFile)?.LastFailure;
        }
    }

    private void RememberFailure(SitePaths paths, Error? failure)
    {
        lock (_memoryGate)
        {
            Memory(paths).LastFailure = failure;
        }
    }

    private void ResetMemory(SitePaths paths)
    {
        lock (_memoryGate)
        {
            _memory.Remove(paths.RunningFile);
        }
    }

    // RCON refuses connections until the world is loaded; probing at most every 2 s keeps frequent status calls cheap.
    private bool TryClaimProbe(SitePaths paths)
    {
        var now = time.GetUtcNow();
        lock (_memoryGate)
        {
            var memory = Memory(paths);
            if (memory.LastProbe is { } last && now - last < ReadyProbeInterval)
            {
                return false;
            }

            memory.LastProbe = now;
            return true;
        }
    }

    private SiteMemory Memory(SitePaths paths)
    {
        if (!_memory.TryGetValue(paths.RunningFile, out var memory))
        {
            memory = new SiteMemory();
            _memory[paths.RunningFile] = memory;
        }

        return memory;
    }

    /// <param name="Alive">The record of a process that still runs; <c>null</c> when stopped.</param>
    private sealed record Observation(ServerStatus Status, RunningServer? Alive);

    // Not persisted: a restarted host forgets the last failure and probes again at once.
    private sealed class SiteMemory
    {
        public Error? LastFailure { get; set; }

        public DateTimeOffset? LastProbe { get; set; }
    }
}
