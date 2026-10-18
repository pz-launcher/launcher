using PzLauncher.Core.Common;

namespace PzLauncher.Core.Update;

/// <summary>Updates the running launcher in place from GitHub or Gitee releases; stable releases only.</summary>
public interface IUpdateService
{
    SemVersion CurrentVersion { get; }

    /// <summary>A <c>0.0.0-*</c> build: check, download and install fail with <c>update.development_build</c>.</summary>
    bool IsDevelopmentBuild { get; }

    /// <summary>Whether the launcher's folder accepts new files; probes the disk on each read.</summary>
    bool CanInstallInPlace { get; }

    /// <summary><see cref="UpdateSource.Auto"/> uses whichever source answers first and fails only when both fail.</summary>
    Task<Result<UpdateCheck>> CheckAsync(UpdateSource source, CancellationToken ct = default);

    /// <summary>Downloads, verifies and extracts next to the launcher; returns the staged copy at once when this version is already staged.</summary>
    Task<Result<StagedUpdate>> DownloadAsync(
        ReleaseInfo release, IProgress<DownloadProgress>? progress = null, CancellationToken ct = default);

    /// <summary>The newest staged update that is newer than the running version; <c>null</c> when none (an unreadable staging folder is logged and counts as none).</summary>
    Task<StagedUpdate?> GetStagedAsync(CancellationToken ct = default);

    /// <summary>Replaces only the files the package contains; on failure every replaced file is restored.</summary>
    Task<Result> InstallAsync(StagedUpdate staged, CancellationToken ct = default);

    /// <summary>Starts the launcher executable in the install folder (after <see cref="InstallAsync"/>, the new version); the host exits next.</summary>
    Task<Result> StartInstalledAsync(CancellationToken ct = default);

    /// <summary>Deletes files a previous install moved aside, partial downloads and stale staged packages; never fails, logs what it can't delete.</summary>
    Task CleanUpLeftoversAsync(CancellationToken ct = default);
}
