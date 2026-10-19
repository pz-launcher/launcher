using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PzLauncher.Core.Common;
using PzLauncher.Core.Installs;
using PzLauncher.Core.Launch;
using PzLauncher.Core.Platform;
using PzLauncher.Core.Steam.Workshop.Helper;
using PzLauncher.Core.Update;

namespace PzLauncher.Core.Steam.Workshop.Actions;

internal sealed partial class WorkshopActionRunner(
    IPlatformEnvironment platform,
    IProcessRunner processes,
    TimeProvider time,
    ILogger<WorkshopActionRunner> logger) : IWorkshopActionRunner
{
    // Two helpers would both hold the game's app id in Steam; the second simply waits its turn.
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>The launcher's own executable, which runs the helper; <c>null</c> when the OS doesn't report it.</summary>
    internal string? HelperExecutable { get; init; } = Environment.ProcessPath;

    // Longer than the helper's own per-item stall timeout (SteamHelperSession), so the helper reports a stuck item first.
    internal TimeSpan StallTimeout { get; init; } = TimeSpan.FromMinutes(3);

    internal TimeSpan ExitTimeout { get; init; } = TimeSpan.FromSeconds(10);

    public async Task<Result<WorkshopActionOutcome>> RunAsync(
        string installRoot, WorkshopActionRequest request, IProgress<WorkshopActionEvent>? progress = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!PathNormalizer.TryNormalizeAbsolute(installRoot, out var root))
        {
            return Logged(InstallErrors.InvalidPath(installRoot));
        }

        var ids = request.Ids.Distinct().ToList();
        if (ids.Count == 0)
        {
            return new WorkshopActionOutcome([]);
        }

        if (WorkshopFiles.SteamApiCandidates(root, platform.Os).FirstOrDefault(File.Exists) is not { } steamApi)
        {
            return Logged(WorkshopErrors.SteamApiMissing(root));
        }

        if (HelperExecutable is not { } executable)
        {
            return Logged(WorkshopErrors.HelperStartFailed(AppContext.BaseDirectory, "the launcher's own executable path is unknown"));
        }

        var helperRequest = new SteamHelperRequest(request.Action, steamApi, ids);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // The game folder holds the game's steam_appid.txt, Steam's fallback when the variables don't reach it.
            return await RunHelperAsync(executable, PzLaunchFiles.ResolveGameFolder(root), helperRequest, progress, ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<Result<WorkshopActionOutcome>> RunHelperAsync(
        string executable, string workingDirectory, SteamHelperRequest request, IProgress<WorkshopActionEvent>? progress, CancellationToken ct)
    {
        var appId = PzSteamApps.Game.ToString(CultureInfo.InvariantCulture);
        var environment = WorkshopFiles.AppIdVariables.ToDictionary(name => name, _ => appId, StringComparer.Ordinal);
        IPipedProcess process;
        try
        {
            process = processes.StartPiped(new ProcessStartRequest(executable, [LauncherArguments.SteamHelper], workingDirectory, environment));
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException)
        {
            return Logged(WorkshopErrors.HelperStartFailed(executable, ex.Message));
        }

        using (process)
        {
            try
            {
                return await ConverseAsync(process, request, progress, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                await StopAsync(process).ConfigureAwait(false);
                throw;
            }
            finally
            {
                // Steam's library writes its own notes to stderr; they never decide the outcome.
                foreach (var line in process.ErrorOutputTail)
                {
                    LogHelperStderr(logger, line);
                }
            }
        }
    }

    private async Task<Result<WorkshopActionOutcome>> ConverseAsync(
        IPipedProcess process, SteamHelperRequest request, IProgress<WorkshopActionEvent>? progress, CancellationToken ct)
    {
        try
        {
            await process.SendInputAsync(JsonSerializer.Serialize(request, SteamHelperJsonContext.Default.SteamHelperRequest) + "\n", ct)
                .ConfigureAwait(false);
        }
        catch (IOException ex)
        {
            // The helper is already gone; its missing outcome is reported below.
            LogInputFailed(logger, ex.Message);
        }

        var outcomes = new Dictionary<ulong, WorkshopItemOutcome>();
        Error? failure = null;
        var finished = false;
        while (true)
        {
            string? line;
            try
            {
                line = await process.ReadLineAsync(ct).WaitAsync(StallTimeout, time, ct).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                await StopAsync(process).ConfigureAwait(false);
                return Logged(WorkshopErrors.HelperTimedOut(StallTimeout));
            }

            if (line is null)
            {
                break;
            }

            switch (Parse(line))
            {
                case SteamHelperStarted:
                    progress?.Report(new WorkshopActionStarted());
                    break;
                case SteamHelperProgress p:
                    progress?.Report(new WorkshopItemProgress(p.Id, p.Bytes, p.Total));
                    break;
                case SteamHelperItemDone done:
                    Finish(new WorkshopItemOutcome(done.Id, null, 0));
                    break;
                case SteamHelperItemFailed failed:
                    Finish(new WorkshopItemOutcome(failed.Id, failed.Reason, failed.SteamResult));
                    break;
                case SteamHelperFailed failed:
                    failure = ToError(failed, request.SteamApiPath);
                    break;
                case SteamHelperFinished:
                    finished = true;
                    break;
                case null:
                    break;
            }
        }

        var exited = await TryWaitForExitAsync(process, ct).ConfigureAwait(false);
        if (!exited)
        {
            await StopAsync(process).ConfigureAwait(false);
        }

        if (failure is not null)
        {
            return Logged(failure);
        }

        if (!finished)
        {
            return Logged(WorkshopErrors.HelperFailed(exited
                ? $"exited with code {process.ExitCode.ToString(CultureInfo.InvariantCulture)} before finishing"
                : "closed its output before finishing"));
        }

        return new WorkshopActionOutcome(
            [.. request.Ids.Select(id => outcomes.GetValueOrDefault(id) ?? new WorkshopItemOutcome(id, WorkshopItemFailure.NotReported, 0))]);

        void Finish(WorkshopItemOutcome outcome)
        {
            outcomes[outcome.Id] = outcome;
            progress?.Report(new WorkshopItemFinished(outcome));
        }
    }

    private static Error ToError(SteamHelperFailed failed, string steamApi) => failed.Reason switch
    {
        SteamHelperFailure.LibraryNotLoaded or SteamHelperFailure.ExportMissing => WorkshopErrors.SteamApiUnusable(steamApi, failed.Message),
        SteamHelperFailure.SteamNotRunning => WorkshopErrors.SteamNotRunning(),

        // VersionMismatch: the Steam client is older than the game's library.
        SteamHelperFailure.VersionMismatch or SteamHelperFailure.InitFailed => WorkshopErrors.SteamInitFailed(failed.Message),
        SteamHelperFailure.BadRequest => WorkshopErrors.HelperFailed(failed.Message),
    };

    private SteamHelperEvent? Parse(string line)
    {
        try
        {
            return JsonSerializer.Deserialize(line, SteamHelperJsonContext.Default.SteamHelperEvent);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            // NotSupportedException: no or an unknown "event" discriminator on the abstract base.
            LogUnreadableLine(logger, line, ex.Message);
            return null;
        }
    }

    private async Task StopAsync(IPipedProcess process)
    {
        process.Kill();
        await TryWaitForExitAsync(process, CancellationToken.None).ConfigureAwait(false);
    }

    private async Task<bool> TryWaitForExitAsync(IPipedProcess process, CancellationToken ct)
    {
        try
        {
            await process.WaitForExitAsync(ct).WaitAsync(ExitTimeout, time, ct).ConfigureAwait(false);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    private Error Logged(Error error)
    {
        LogFailed(logger, error.Code.ToWireName(), error.Message);
        return error;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Steam Workshop action: {Code}: {Detail}")]
    private static partial void LogFailed(ILogger logger, string code, string detail);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Steam helper: unreadable output line skipped ({Reason}): {Line}")]
    private static partial void LogUnreadableLine(ILogger logger, string line, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Steam helper: request not delivered ({Reason})")]
    private static partial void LogInputFailed(ILogger logger, string reason);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Steam helper stderr: {Line}")]
    private static partial void LogHelperStderr(ILogger logger, string line);
}
