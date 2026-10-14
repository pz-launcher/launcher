namespace PzLauncher.Core.Installs;

/// <param name="RootPath">Absolute and normalized.</param>
/// <param name="Steam"><c>null</c> for a manually added folder.</param>
public sealed record PzInstall(
    PzInstallKind Kind,
    string RootPath,
    SteamAppInfo? Steam)
{
    public PzInstallSource Source => Steam is null ? PzInstallSource.Manual : PzInstallSource.SteamLibrary;
}
