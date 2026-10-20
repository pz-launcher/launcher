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

    // Sent to the helper, so it gives up on a stuck item before the launcher gives up on the helper.
    internal SteamHelperTimeouts HelperTimeouts { get; init; } = SteamHelperTimeouts.Default;

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

        var outcomes = new Dictionary<ulong, WorkshopItemOutcome>();
        var run = await RunHelperAsync(root, ToHelperAction(request.Action), ids, progress, OnEvent, ct).ConfigureAwait(false);
        if (!run.IsSuccess)
        {
            return run.Error;
        }

        return new WorkshopActionOutcome(
            [.. ids.Select(id => outcomes.GetValueOrDefault(id) ?? new WorkshopItemOutcome(id, WorkshopItemFailure.NotReported, 0))]);

        void OnEvent(SteamHelperEvent e)
        {
            switch (e)
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
                    Finish(new WorkshopItemOutcome(failed.Id, ToItemFailure(failed.Reason), failed.SteamResult));
                    break;
            }
        }

        void Finish(WorkshopItemOutcome outcome)
        {
            outcomes[outcome.Id] = outcome;
            progress?.Report(new WorkshopItemFinished(outcome));
        }
    }

    public async Task<Result<WorkshopUserCollections>> ListCollectionsAsync(
        string installRoot, IProgress<WorkshopActionEvent>? progress = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!PathNormalizer.TryNormalizeAbsolute(installRoot, out var root))
        {
            return Logged(InstallErrors.InvalidPath(installRoot));
        }

        WorkshopUserCollections? collections = null;
        var run = await RunHelperAsync(root, SteamHelperAction.ListCollections, [], progress, OnEvent, ct).ConfigureAwait(false);
        if (!run.IsSuccess)
        {
            return run.Error;
        }

        if (collections is null)
        {
            return Logged(WorkshopErrors.HelperFailed("finished without listing the collections"));
        }

        return collections;

        void OnEvent(SteamHelperEvent e)
        {
            if (e is SteamHelperCollections listed)
            {
                collections = new WorkshopUserCollections(listed.AccountId, ToSummaries(listed.Favorited), ToSummaries(listed.Published));
            }
        }
    }

    private static SteamHelperAction ToHelperAction(WorkshopAction action) => action switch
    {
        WorkshopAction.Subscribe => SteamHelperAction.Subscribe,
        WorkshopAction.Unsubscribe => SteamHelperAction.Unsubscribe,
        WorkshopAction.Update => SteamHelperAction.Update,
    };

    private static WorkshopItemFailure ToItemFailure(SteamHelperItemFailure failure) => failure switch
    {
        SteamHelperItemFailure.SubscribeFailed => WorkshopItemFailure.SubscribeFailed,
        SteamHelperItemFailure.UnsubscribeFailed => WorkshopItemFailure.UnsubscribeFailed,
        SteamHelperItemFailure.DownloadNotStarted => WorkshopItemFailure.DownloadNotStarted,
        SteamHelperItemFailure.DownloadFailed => WorkshopItemFailure.DownloadFailed,
        SteamHelperItemFailure.TimedOut => WorkshopItemFailure.TimedOut,
    };

    private static List<WorkshopCollectionSummary> ToSummaries(IReadOnlyList<SteamHelperCollection> collections) =>
        collections.Select(collection => new WorkshopCollectionSummary(collection.Id, collection.Title, collection.ChildCount)).ToList();

    // A working helper writes at least this often, with room for Steam's start and shutdown.
    private TimeSpan StallTimeout => HelperTimeouts.LongestSilence + TimeSpan.FromSeconds(30);

    /// <summary>Runs the helper for a normalized install root, passing it every event but <c>failed</c> and <c>finished</c>.</summary>
    /// <returns>Success once the helper finished; otherwise its failure, already logged.</returns>
    private async Task<Result> RunHelperAsync(
        string root,
        SteamHelperAction action,
        IReadOnlyList<ulong> ids,
        IProgress<WorkshopActionEvent>? progress,
        Action<SteamHelperEvent> onEvent,
        CancellationToken ct)
    {
        if (WorkshopFiles.SteamApiCandidates(root, platform.Os).FirstOrDefault(File.Exists) is not { } steamApi)
        {
            return Logged(WorkshopErrors.SteamApiMissing(root));
        }

        if (HelperExecutable is not { } executable)
        {
            return Logged(WorkshopErrors.HelperStartFailed(AppContext.BaseDirectory, "the launcher's own executable path is unknown"));
        }

        var request = new SteamHelperRequest(action, steamApi, ids, HelperTimeouts);
        if (!_gate.Wait(0, ct))
        {
            progress?.Report(new WorkshopActionWaiting());
            await _gate.WaitAsync(ct).ConfigureAwait(false);
        }

        try
        {
            // The game folder holds the game's steam_appid.txt, Steam's fallback when the variables don't reach it.
            return await StartHelperAsync(executable, PzLaunchFiles.ResolveGameFolder(root), request, onEvent, ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<Result> StartHelperAsync(
        string executable, string workingDirectory, SteamHelperRequest request, Action<SteamHelperEvent> onEvent, CancellationToken ct)
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
                return await ConverseAsync(process, request, onEvent, ct).ConfigureAwait(false);
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

    private async Task<Result> ConverseAsync(IPipedProcess process, SteamHelperRequest request, Action<SteamHelperEvent> onEvent, CancellationToken ct)
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
                case SteamHelperFailed failed:
                    failure = ToError(failed, request.SteamApiPath);
                    break;
                case SteamHelperFinished:
                    finished = true;
                    break;
                case { } e:
                    onEvent(e);
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

        return Result.Ok();
    }

    private static Error ToError(SteamHelperFailed failed, string steamApi) => failed.Reason switch
    {
        SteamHelperFailure.LibraryNotLoaded or SteamHelperFailure.ExportMissing => WorkshopErrors.SteamApiUnusable(steamApi, failed.Message),
        SteamHelperFailure.SteamNotRunning => WorkshopErrors.SteamNotRunning(),

        // VersionMismatch: the Steam client is older than the game's library.
        SteamHelperFailure.VersionMismatch or SteamHelperFailure.InitFailed => WorkshopErrors.SteamInitFailed(failed.Message),
        SteamHelperFailure.BadRequest => WorkshopErrors.HelperRequestRejected(failed.Message),
        SteamHelperFailure.QueryFailed => WorkshopErrors.CollectionsQueryFailed(failed.Message),
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
