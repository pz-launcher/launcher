using System.ComponentModel;
using System.Globalization;
using Microsoft.Extensions.Logging;
using PzLauncher.Core.Common;
using PzLauncher.Core.Platform.Processes;
using PzLauncher.Core.Platform;
using PzLauncher.Core.Update.Releases;

namespace PzLauncher.Core.Update;

internal sealed partial class UpdateService
{
    public async Task<Result> InstallAsync(StagedUpdate staged, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (current.IsDevelopment)
        {
            return Logged(UpdateErrors.DevelopmentBuild(current));
        }

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Trust only the layout we wrote, never the folder named in the DTO.
            var paths = Paths;
            var source = paths.StagedContent(staged.Version);
            var marker = paths.ReadyMarker(staged.Version);
            if (!File.Exists(marker) || !Directory.Exists(source))
            {
                return Logged(UpdateErrors.PackageInvalid(staged.Version.ToString(), "not staged"));
            }

            var backup = Path.Combine(paths.Old, time.GetUtcNow().ToString("yyyyMMdd'T'HHmmssfff", CultureInfo.InvariantCulture));
            if (Directory.Exists(backup))
            {
                backup += "-" + Guid.NewGuid().ToString("N");
            }

            // No cancellation from here on: a half-replaced install is worse than a finished one, and renames are quick.
            var oldExisted = Directory.Exists(paths.Old);
            var steps = new List<InstallStep>();
            try
            {
                foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories).ToList())
                {
                    var relative = Path.GetRelativePath(source, file);
                    var target = Path.Combine(InstallDirectory, relative);
                    CreateParents(target, steps);
                    if (File.Exists(target))
                    {
                        var aside = Path.Combine(backup, relative);
                        Directory.CreateDirectory(Path.GetDirectoryName(aside) ?? backup);
                        MoveFile(target, aside);
                        steps.Add(new InstallStep(InstallStepKind.MovedAside, target, aside));
                    }

                    MoveFile(file, target);
                    steps.Add(new InstallStep(InstallStepKind.Placed, file, target));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A file rollback couldn't restore survives only in the backup.
                if (RollBack(steps))
                {
                    TryDeleteDirectory(oldExisted ? backup : paths.Old);
                }

                return Logged(UpdateErrors.InstallFailed(InstallDirectory, ex.Message));
            }

            // The marker goes first so a folder that can't be deleted now is never offered again.
            TryDeleteFile(marker);
            TryDeleteDirectory(paths.StagedDirectory(staged.Version));
            return Result.Ok();
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<Result> StartInstalledAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var executable = Path.Combine(InstallDirectory, LauncherReleases.ExecutableName(platform.Os));
        try
        {
            // Only the start matters; the handle goes at once and the new launcher outlives this one.
            using var process = processes.Start(new ProcessStartRequest(executable, [LauncherArguments.Restart], InstallDirectory, new Dictionary<string, string>()));
            return Task.FromResult(Result.Ok());
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException)
        {
            return Task.FromResult<Result>(Logged(UpdateErrors.RestartFailed(executable, ex.Message)));
        }
    }

    private static void CreateParents(string target, List<InstallStep> steps)
    {
        var missing = new Stack<string>();
        for (var dir = Path.GetDirectoryName(target); dir is not null && !Directory.Exists(dir); dir = Path.GetDirectoryName(dir))
        {
            missing.Push(dir);
        }

        foreach (var dir in missing)
        {
            Directory.CreateDirectory(dir);
            steps.Add(new InstallStep(InstallStepKind.CreatedDirectory, dir, dir));
        }
    }

    private bool RollBack(List<InstallStep> steps)
    {
        var restored = true;
        for (var i = steps.Count - 1; i >= 0; i--)
        {
            var step = steps[i];
            try
            {
                switch (step.Kind)
                {
                    case InstallStepKind.CreatedDirectory:
                        Directory.Delete(step.To);
                        break;
                    case InstallStepKind.MovedAside:
                    case InstallStepKind.Placed:
                        MoveFile(step.To, step.From);
                        break;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                LogRollBackFailed(logger, step.From, ex.Message);
                restored = false;
            }
        }

        return restored;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Update: rollback could not restore {Path}: {Reason}")]
    private static partial void LogRollBackFailed(ILogger logger, string path, string reason);

    private enum InstallStepKind
    {
        CreatedDirectory,

        /// <summary>An install file moved under <c>.update/old</c>.</summary>
        MovedAside,

        /// <summary>A staged file moved into the install.</summary>
        Placed,
    }

    /// <summary>Undo moves <see cref="To"/> back to <see cref="From"/>; a created directory has both set to itself.</summary>
    private sealed record InstallStep(InstallStepKind Kind, string From, string To);
}
