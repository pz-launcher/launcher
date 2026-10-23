using System.ComponentModel;
using System.Text;
using System.Text.Json.Serialization.Metadata;
using System.Text.Json;
using PzLauncher.Core.Common;
using PzLauncher.Core.Java;
using PzLauncher.Core.Server.ConsoleLog;

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
        var changeGate = Memory(paths).ChangeGate;
        RunningServer running;
        await changeGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if ((await ObserveAsync(paths, ct).ConfigureAwait(false)).Alive is not { } alive)
            {
                return ServerErrors.NotRunning();
            }

            running = alive;

            // Already stopping (an earlier stop timed out, or one is still waiting): wait too, without a second quit.
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
        }
        finally
        {
            changeGate.Release();
        }

        // Outside the change gate: a kill mustn't queue behind a save that can take minutes.
        return await WaitForExitAsync(paths, running, StopTimeout, ct).ConfigureAwait(false);
    }

    public async Task<Result<ServerStatus>> KillAsync(ServerSite site, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var paths = SitePaths.Of(site);
        var changeGate = Memory(paths).ChangeGate;
        await changeGate.WaitAsync(ct).ConfigureAwait(false);
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
            changeGate.Release();
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
        var site = Memory(paths);
        RunningServer? running;
        await site.FileGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            running = await ReadStateFileAsync(paths.RunningFile, RunningServerJsonContext.Default.RunningServer, ct).ConfigureAwait(false);
            if (running is null)
            {
                return new Observation(Stopped(await LastFailureAsync(paths, site, ct).ConfigureAwait(false)), null);
            }

            if (!IsAlive(running))
            {
                var failure = await ExitFailureAsync(running, ct).ConfigureAwait(false);
                DeleteStateFile(paths.RunningFile);
                await SetLastFailureAsync(paths, site, failure).ConfigureAwait(false);
                if (failure is null)
                {
                    LogStopped(logger, running.Name);
                }
                else
                {
                    Failed(failure);
                }

                return new Observation(Stopped(failure), null);
            }
        }
        finally
        {
            site.FileGate.Release();
        }

        if (running.StopRequestedAt is null && !running.Ready && TryClaimProbe(site)
            && (await rcon.AuthenticateAsync(running.RconPort, running.RconPassword, ct).ConfigureAwait(false)).IsSuccess)
        {
            running = await MarkReadyAsync(paths, running, ct).ConfigureAwait(false);
        }

        return new Observation(StatusOf(running), running);
    }

    private static ServerStatus Stopped(Error? lastFailure) => new(ServerState.Stopped, null, null, null, lastFailure);

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

        var site = Memory(paths);
        await site.FileGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            // A start that came in after the exit owns the files now.
            var current = await ReadStateFileAsync(paths.RunningFile, RunningServerJsonContext.Default.RunningServer, CancellationToken.None)
                .ConfigureAwait(false);
            if (current is null || IsSameRun(current, running))
            {
                if (current is not null)
                {
                    DeleteStateFile(paths.RunningFile);
                }

                await SetLastFailureAsync(paths, site, null).ConfigureAwait(false);
            }
        }
        finally
        {
            site.FileGate.Release();
        }

        LogStopped(logger, running.Name);
        return Stopped(null);
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
        var fileGate = Memory(paths).FileGate;
        await fileGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var type = RunningServerJsonContext.Default.RunningServer;
            if (await ReadStateFileAsync(paths.RunningFile, type, ct).ConfigureAwait(false) is { } current && IsSameRun(current, expected))
            {
                await WriteStateFileAsync(paths.RunningFile, updated, type, ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogStateFileProblem(logger, paths.RunningFile, ex.Message);
        }
        finally
        {
            fileGate.Release();
        }
    }

    private static bool IsSameRun(RunningServer a, RunningServer b) =>
        a.ProcessId == b.ProcessId && a.ProcessStartTime == b.ProcessStartTime;

    // Under the site's file gate. Read once per site, then kept in step with the file.
    private async Task<Error?> LastFailureAsync(SitePaths paths, SiteMemory site, CancellationToken ct)
    {
        if (!site.LastFailureLoaded)
        {
            site.LastFailure = await ReadStateFileAsync(paths.LastFailureFile, RunningServerJsonContext.Default.Error, ct).ConfigureAwait(false);
            site.LastFailureLoaded = true;
        }

        return site.LastFailure;
    }

    // Under the site's file gate. A failed write or delete only costs what a restarted host reports.
    private async Task SetLastFailureAsync(SitePaths paths, SiteMemory site, Error? failure)
    {
        site.LastFailure = failure;
        site.LastFailureLoaded = true;
        if (failure is null)
        {
            DeleteStateFile(paths.LastFailureFile);
            return;
        }

        try
        {
            await WriteStateFileAsync(paths.LastFailureFile, failure, RunningServerJsonContext.Default.Error, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogStateFileProblem(logger, paths.LastFailureFile, ex.Message);
        }
    }

    // Missing means none. Unreadable is treated as none too, but kept; malformed can never become readable, so it goes.
    private async Task<T?> ReadStateFileAsync<T>(string path, JsonTypeInfo<T> type, CancellationToken ct)
        where T : class
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

        T? value = null;
        var problem = "The file holds no JSON object.";
        try
        {
            value = JsonSerializer.Deserialize(bytes.Value, type);
        }
        catch (JsonException ex)
        {
            problem = ex.Message;
        }

        if (value is null)
        {
            LogStateFileProblem(logger, path, problem);
            DeleteStateFile(path);
        }

        return value;
    }

    private static async Task WriteStateFileAsync<T>(string path, T value, JsonTypeInfo<T> type, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? path);
        await AtomicFile.WriteAsync(path, JsonSerializer.SerializeToUtf8Bytes(value, type), ct).ConfigureAwait(false);
    }

    private void DeleteStateFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A running file is deleted again by the next status check that finds the process gone.
            LogStateFileProblem(logger, path, ex.Message);
        }
    }

    // RCON refuses connections until the world is loaded; probing at most every 2 s keeps frequent status calls cheap.
    private bool TryClaimProbe(SiteMemory site)
    {
        var now = time.GetUtcNow();
        lock (_memoryGate)
        {
            if (site.LastProbe is { } last && now - last < ReadyProbeInterval)
            {
                return false;
            }

            site.LastProbe = now;
            return true;
        }
    }

    private void ResetProbe(SiteMemory site)
    {
        lock (_memoryGate)
        {
            site.LastProbe = null;
        }
    }

    private SiteMemory Memory(SitePaths paths)
    {
        lock (_memoryGate)
        {
            if (!_memory.TryGetValue(paths.RunningFile, out var memory))
            {
                memory = new SiteMemory();
                _memory[paths.RunningFile] = memory;
            }

            return memory;
        }
    }

    /// <param name="Alive">The record of a process that still runs; <c>null</c> when stopped.</param>
    private sealed record Observation(ServerStatus Status, RunningServer? Alive);

    // One per state folder for the service's lifetime, so its gates are never replaced while held.
    private sealed class SiteMemory
    {
        // One change (create, start, stop, kill) at a time; status, commands and a stop's wait don't hold it.
        public SemaphoreSlim ChangeGate { get; } = new(1, 1);

        // Around every read-modify-write of the running and last-failure files, and LastFailure with them.
        public SemaphoreSlim FileGate { get; } = new(1, 1);

        public bool LastFailureLoaded { get; set; }

        public Error? LastFailure { get; set; }

        // Not persisted: a restarted host probes again at once.
        public DateTimeOffset? LastProbe { get; set; }
    }
}
