namespace PzLauncher.Core.Update;

/// <summary>The <c>.update</c> layout below an install folder.</summary>
internal sealed record UpdatePaths(string InstallDirectory)
{
    public string Root => Path.Combine(InstallDirectory, LauncherReleases.UpdateFolder);

    public string Download => Path.Combine(Root, LauncherReleases.DownloadFolder);

    public string Staged => Path.Combine(Root, LauncherReleases.StagedFolder);

    public string Old => Path.Combine(Root, LauncherReleases.OldFolder);

    public string StagedDirectory(SemVersion version) => Path.Combine(Staged, version.ToString());

    // Written last: a staged folder without it is an interrupted extraction.
    public string ReadyMarker(SemVersion version) => StagedDirectory(version) + LauncherReleases.ReadyMarkerSuffix;
}
