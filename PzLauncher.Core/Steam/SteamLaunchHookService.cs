using System.ComponentModel;
using Microsoft.Extensions.Logging;
using PzLauncher.Core.Common;
using PzLauncher.Core.Platform;

namespace PzLauncher.Core.Steam;

internal sealed partial class SteamLaunchHookService(
    SteamLocator locator,
    IPlatformEnvironment platform,
    IProcessRunner processes,
    TimeProvider time,
    ILogger<SteamLaunchHookService> logger) : ISteamLaunchHookService
{
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan ShutdownPollInterval = TimeSpan.FromMilliseconds(500);

    public async Task<Result<SteamLaunchHookStatus>> GetStatusAsync(string launcherExe, CancellationToken ct = default)
    {
        var exe = Absolute(launcherExe);
        var target = await LocateAsync(ct).ConfigureAwait(false);
        if (!target.IsSuccess)
        {
            return Logged(target.Error);
        }

        var config = await LocalConfigEditor.ReadAsync(target.Value.ConfigPath, PzSteamApps.Game, ct).ConfigureAwait(false);
        return config.IsSuccess
            ? Status(target.Value.Account, config.Value.LaunchOptions, exe, IsSteamRunning())
            : Logged(config.Error);
    }

    public async Task<Result<SteamLaunchHookApplied>> SetAsync(
        string launcherExe, bool enabled, bool restartSteam, CancellationToken ct = default)
    {
        var exe = Absolute(launcherExe);
        var target = await LocateAsync(ct).ConfigureAwait(false);
        if (!target.IsSuccess)
        {
            return Logged(target.Error);
        }

        var (root, account, configPath) = target.Value;

        // Read first, so a file that can't be edited never costs the user a Steam restart.
        var config = await LocalConfigEditor.ReadAsync(configPath, PzSteamApps.Game, ct).ConfigureAwait(false);
        if (!config.IsSuccess)
        {
            return Logged(config.Error);
        }

        var running = IsSteamRunning();
        if (Transform(config.Value.LaunchOptions, exe, enabled) == config.Value.LaunchOptions)
        {
            return new SteamLaunchHookApplied(Status(account, config.Value.LaunchOptions, exe, running), []);
        }

        if (running && !restartSteam)
        {
            return Logged(SteamErrors.ClientRunning());
        }

        if (running && Run(root, [SteamClientFiles.ShutdownArgument]) is { IsSuccess: false } shutdown)
        {
            return Logged(shutdown.Error);
        }

        string options;
        var warnings = new List<Error>();
        try
        {
            if (running && await WaitForSteamExitAsync(ct).ConfigureAwait(false) is { IsSuccess: false } exited)
            {
                return Logged(exited.Error);
            }

            // Re-read: Steam rewrites the file when it exits, and every retry starts from what is on disk.
            config = await LocalConfigEditor.ReadAsync(configPath, PzSteamApps.Game, ct).ConfigureAwait(false);
            if (!config.IsSuccess)
            {
                return Logged(config.Error);
            }

            options = Transform(config.Value.LaunchOptions, exe, enabled);
            var written = await config.Value.WriteAsync(options, ct).ConfigureAwait(false);
            if (!written.IsSuccess)
            {
                return Logged(written.Error);
            }
        }
        finally
        {
            // Also after a failure or cancellation: the user never asked to be left without Steam.
            if (running && StartSteam(root) is { IsSuccess: false } started)
            {
                warnings.Add(Logged(started.Error));
            }
        }

        LogApplied(logger, enabled, account.AccountId);
        return new SteamLaunchHookApplied(Status(account, options, exe, running && warnings.Count == 0), warnings);
    }

    private static string Absolute(string launcherExe) =>
        PathNormalizer.TryNormalizeAbsolute(launcherExe, out var exe)
            ? exe
            : throw new ArgumentException("The launcher path must be absolute.", nameof(launcherExe));

    private string Transform(string options, string exe, bool enabled) =>
        !enabled ? SteamLaunchOptions.RemoveHook(options, exe)
        : IsThisLauncher(SteamLaunchOptions.FindHookPath(options, exe), exe) ? options
        : SteamLaunchOptions.AddHook(options, exe);

    private SteamLaunchHookStatus Status(SteamAccount account, string options, string exe, bool running)
    {
        var hooked = SteamLaunchOptions.FindHookPath(options, exe);
        return hooked is null ? new SteamLaunchHookStatus(account, SteamLaunchHookState.Off, null, running)
            : IsThisLauncher(hooked, exe) ? new SteamLaunchHookStatus(account, SteamLaunchHookState.On, null, running)
            : new SteamLaunchHookStatus(account, SteamLaunchHookState.OtherLauncher, hooked, running);
    }

    private bool IsThisLauncher(string? hooked, string exe) =>
        PathNormalizer.TryNormalizeAbsolute(hooked, out var normalized) && string.Equals(normalized, exe, platform.PathComparison);

    private async Task<Result<SteamTarget>> LocateAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (platform.Os != PlatformKind.Windows)
        {
            return SteamErrors.PlatformUnsupported(platform.Os);
        }

        if (locator.FindSteamRoots() is not [var root, ..])
        {
            return SteamErrors.NotInstalled();
        }

        var account = await SteamLoginUsers.ReadCurrentAsync(SteamClientFiles.LoginUsersPath(root), ct).ConfigureAwait(false);
        return account.IsSuccess
            ? new SteamTarget(root, account.Value, SteamClientFiles.LocalConfigPath(root, account.Value.AccountId))
            : account.Error;
    }

    private bool IsSteamRunning()
    {
        var running = processes.FindByName(SteamClientFiles.ProcessName);
        foreach (var process in running)
        {
            process.Dispose();
        }

        return running.Count > 0;
    }

    // Polled, not waited on: a Steam running elevated denies the handle a wait needs.
    private async Task<Result> WaitForSteamExitAsync(CancellationToken ct)
    {
        var started = time.GetTimestamp();
        while (true)
        {
            try
            {
                if (!IsSteamRunning())
                {
                    return Result.Ok();
                }
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or UnauthorizedAccessException)
            {
                return SteamErrors.ShutdownUnconfirmed(ex.Message);
            }

            if (time.GetElapsedTime(started) >= ShutdownTimeout)
            {
                return SteamErrors.ShutdownTimedOut(ShutdownTimeout);
            }

            await Task.Delay(ShutdownPollInterval, time, ct).ConfigureAwait(false);
        }
    }

    private Result StartSteam(string root) => Run(root, []);

    private Result Run(string root, IReadOnlyList<string> arguments)
    {
        var exe = Path.Combine(root, SteamClientFiles.WindowsExecutable);
        try
        {
            // Only the start matters; Steam outlives the handle and the launcher.
            using var process = processes.Start(new ProcessStartRequest(exe, arguments, root, new Dictionary<string, string>()));
            return Result.Ok();
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException)
        {
            return SteamErrors.StartFailed(exe, ex.Message);
        }
    }

    private Error Logged(Error error)
    {
        LogFailed(logger, error.Code.ToWireName(), error.Message);
        return error;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Steam launch hook: {Code}: {Detail}")]
    private static partial void LogFailed(ILogger logger, string code, string detail);

    [LoggerMessage(Level = LogLevel.Information, Message = "Steam launch hook set to {Enabled} for account {AccountId}")]
    private static partial void LogApplied(ILogger logger, bool enabled, uint accountId);

    private sealed record SteamTarget(string Root, SteamAccount Account, string ConfigPath);
}
