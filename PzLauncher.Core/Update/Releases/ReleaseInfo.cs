namespace PzLauncher.Core.Update.Releases;

/// <summary>A stable release with a package for the running platform.</summary>
/// <param name="Source">The source that answered: <see cref="UpdateSource.GitHub"/> or <see cref="UpdateSource.Gitee"/>, never <see cref="UpdateSource.Auto"/>.</param>
/// <param name="Size">Bytes; <c>null</c> when the source doesn't publish it.</param>
/// <param name="Sha256">Lowercase hex; <c>null</c> when the source doesn't publish it (Gitee).</param>
public sealed record ReleaseInfo(
    SemVersion Version,
    UpdateSource Source,
    string PackageName,
    Uri PackageUrl,
    long? Size,
    string? Sha256,
    Uri ReleasePageUrl);
