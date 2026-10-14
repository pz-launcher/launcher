using PzLauncher.Core.Common;

namespace PzLauncher.Core.Installs;

/// <summary>Read-only: never writes into Steam or game folders.</summary>
public interface IInstallDetectionService
{
    /// <summary>Problems that don't stop the scan (even "no Steam") are warnings, not failures.</summary>
    Task<Result<InstallScanResult>> ScanAsync(CancellationToken ct = default);

    Task<Result<PzInstall>> InspectAsync(string path, CancellationToken ct = default);
}
