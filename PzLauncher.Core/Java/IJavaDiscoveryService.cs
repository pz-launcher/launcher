using PzLauncher.Core.Common;

namespace PzLauncher.Core.Java;

/// <summary>Read-only; never runs Java. Only 64-bit runtimes are returned.</summary>
public interface IJavaDiscoveryService
{
    /// <summary>Problems that don't stop the scan are warnings; finds nothing on macOS.</summary>
    Task<Result<JavaScanResult>> ScanAsync(CancellationToken ct = default);

    /// <summary>Accepts a Java home or its <c>bin</c> folder; the result's source is <see cref="JavaRuntimeSource.Manual"/>.</summary>
    Task<Result<JavaRuntime>> InspectAsync(string path, CancellationToken ct = default);
}
