using PzLauncher.Core.Installs;

namespace PzLauncher.Desktop.ViewModels.Installs;

public sealed record InstallRow(PzInstall Install, bool IsInUse);
