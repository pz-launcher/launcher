namespace PzLauncher.Core.Update;

/// <summary>A verified, extracted package waiting to be installed.</summary>
public sealed record StagedUpdate(SemVersion Version, string StagingDirectory, Uri ReleasePageUrl);
