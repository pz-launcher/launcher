using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PzLauncher.Core.Common;
using PzLauncher.Core.Net;
using PzLauncher.Core.Platform;

namespace PzLauncher.Core.Update;

internal sealed partial class UpdateService(
    SemVersion current,
    LauncherHttpClient http,
    IPlatformEnvironment platform,
    IProcessRunner processes,
    TimeProvider time,
    ILogger<UpdateService> logger) : IUpdateService
{
    // Download, install and clean-up all rewrite .update; they must not interleave.
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ReleaseFeed _gitHub = new(LauncherReleases.GitHub, http.Client);
    private readonly ReleaseFeed _gitee = new(LauncherReleases.Gitee, http.Client);

    /// <summary>The folder the running launcher was started from.</summary>
    internal string InstallDirectory { get; init; } = PathNormalizer.Normalize(AppContext.BaseDirectory);

    internal Architecture ProcessArchitecture { get; init; } = HostRuntime.ProcessArchitecture;

    internal TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>A download fails when no byte arrives for this long.</summary>
    internal TimeSpan StallTimeout { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>Test seam for injecting a failure mid-install; throws like <see cref="File.Move(string, string)"/>.</summary>
    internal Action<string, string> MoveFile { get; init; } = static (source, target) => File.Move(source, target);

    public SemVersion CurrentVersion => current;

    public bool IsDevelopmentBuild => current.IsDevelopment;

    public bool CanInstallInPlace
    {
        get
        {
            var paths = Paths;
            try
            {
                Directory.CreateDirectory(paths.Root);
                using var probe = new FileStream(
                    Path.Combine(paths.Root, $".probe-{Guid.NewGuid():N}"), FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }
    }

    private UpdatePaths Paths => new(InstallDirectory);

    public async Task<Result<UpdateCheck>> CheckAsync(UpdateSource source, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (current.IsDevelopment)
        {
            return Logged(UpdateErrors.DevelopmentBuild(current));
        }

        var rid = LauncherReleases.RuntimeIdentifier(platform.Os, ProcessArchitecture);
        if (rid is null)
        {
            return new UpdateCheck(current, null);
        }

        var latest = source switch
        {
            UpdateSource.Auto => await RaceAsync(rid, ct).ConfigureAwait(false),
            UpdateSource.GitHub => await FetchAsync(_gitHub, rid, ct).ConfigureAwait(false),
            UpdateSource.Gitee => await FetchAsync(_gitee, rid, ct).ConfigureAwait(false),
        };
        if (!latest.IsSuccess)
        {
            return latest.Error;
        }

        var release = latest.Value.Release;
        return new UpdateCheck(current, release is not null && release.Version > current ? release : null);
    }

    public async Task<StagedUpdate?> GetStagedAsync(CancellationToken ct = default)
    {
        var paths = Paths;
        try
        {
            if (!Directory.Exists(paths.Staged))
            {
                return null;
            }

            StagedUpdate? best = null;
            foreach (var version in ReadyVersions(paths))
            {
                if (version <= current || (best is not null && version <= best.Version))
                {
                    continue;
                }

                var staged = await ReadStagedAsync(paths, version, ct).ConfigureAwait(false);
                best = staged ?? best;
            }

            return best;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogStagingUnreadable(logger, paths.Staged, ex.Message);
            return null;
        }
    }

    public async Task CleanUpLeftoversAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var paths = Paths;
            TryDeleteDirectory(paths.Old);
            TryDeleteDirectory(paths.Download);
            if (!Directory.Exists(paths.Staged))
            {
                return;
            }

            var keep = ReadyVersions(paths).Where(version => version > current).Select(paths.StagedDirectory).ToHashSet(StringComparer.Ordinal);
            foreach (var dir in Directory.EnumerateDirectories(paths.Staged).Where(dir => !keep.Contains(dir)))
            {
                TryDeleteDirectory(dir);
            }

            foreach (var marker in Directory.EnumerateFiles(paths.Staged).Where(file => !keep.Contains(StripMarkerSuffix(file))))
            {
                TryDeleteFile(marker);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogCleanUpFailed(logger, InstallDirectory, ex.Message);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<Result<LatestRelease>> RaceAsync(string rid, CancellationToken ct)
    {
        using var race = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var gitHub = FetchAsync(_gitHub, rid, race.Token);
        var gitee = FetchAsync(_gitee, rid, race.Token);
        var pending = new List<Task<Result<LatestRelease>>> { gitHub, gitee };
        Result<LatestRelease>? withoutPackage = null;
        while (pending.Count > 0)
        {
            var done = await Task.WhenAny(pending).ConfigureAwait(false);
            pending.Remove(done);
            var result = await done.ConfigureAwait(false);
            if (result.IsSuccess && result.Value.Release is not null)
            {
                // The loser ends cancelled, never faulted, so leaving it unobserved is safe.
                await race.CancelAsync().ConfigureAwait(false);
                return result;
            }

            // Gitee publishes a release before its packages finish uploading, so an answer without one waits for the other source.
            if (result.IsSuccess)
            {
                withoutPackage ??= result;
            }
        }

        return withoutPackage ?? await gitHub.ConfigureAwait(false);
    }

    private async Task<Result<LatestRelease>> FetchAsync(ReleaseFeed feed, string rid, CancellationToken ct)
    {
        var result = await feed.GetLatestAsync(rid, RequestTimeout, ct).ConfigureAwait(false);
        return result.IsSuccess ? result : Logged(result.Error);
    }

    // Versions with a ready marker, parsed from the marker's file name.
    private static IEnumerable<SemVersion> ReadyVersions(UpdatePaths paths) =>
        Directory.EnumerateFiles(paths.Staged, "*" + LauncherReleases.ReadyMarkerSuffix)
            .Select(marker => SemVersion.TryParse(Path.GetFileName(StripMarkerSuffix(marker)), out var version) ? version : null)
            .OfType<SemVersion>()
            .Where(version => Directory.Exists(paths.StagedDirectory(version)))
            .ToList();

    private static string StripMarkerSuffix(string path) =>
        path.EndsWith(LauncherReleases.ReadyMarkerSuffix, StringComparison.Ordinal)
            ? path[..^LauncherReleases.ReadyMarkerSuffix.Length]
            : path;

    private async Task<StagedUpdate?> ReadStagedAsync(UpdatePaths paths, SemVersion version, CancellationToken ct)
    {
        var marker = paths.ReadyMarker(version);
        try
        {
            var bytes = await File.ReadAllBytesAsync(marker, ct).ConfigureAwait(false);
            var json = JsonSerializer.Deserialize(bytes, UpdateJsonContext.Default.StagedMarkerJson);
            if (json is not null && Uri.TryCreate(json.ReleasePageUrl, UriKind.Absolute, out var page))
            {
                return new StagedUpdate(version, paths.StagedDirectory(version), page);
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            LogStagingUnreadable(logger, marker, ex.Message);
            return null;
        }

        LogStagingUnreadable(logger, marker, "no release page URL");
        return null;
    }

    private void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The previous process may still hold its files while it exits; the next start retries.
            LogCleanUpFailed(logger, path, ex.Message);
        }
    }

    private void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogCleanUpFailed(logger, path, ex.Message);
        }
    }

    private Error Logged(Error error)
    {
        LogWarning(logger, error.Code.ToWireName(), error.Message);
        return error;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Update: {Code}: {Detail}")]
    private static partial void LogWarning(ILogger logger, string code, string detail);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Update: staged package ignored, {Path} unreadable: {Reason}")]
    private static partial void LogStagingUnreadable(ILogger logger, string path, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Update: could not delete {Path}; retried next start: {Reason}")]
    private static partial void LogCleanUpFailed(ILogger logger, string path, string reason);
}
