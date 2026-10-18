namespace PzLauncher.Core.Update;

/// <param name="Available"><c>null</c>: up to date, or the newer release has no package for this platform.</param>
public sealed record UpdateCheck(SemVersion Current, ReleaseInfo? Available);
