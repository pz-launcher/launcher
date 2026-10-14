using PzLauncher.Core.Common;

namespace PzLauncher.Core.Installs;

public sealed record InstallScanResult(
    IReadOnlyList<PzInstall> Installs,
    IReadOnlyList<Error> Warnings);
