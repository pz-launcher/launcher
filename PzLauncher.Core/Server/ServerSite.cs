namespace PzLauncher.Core.Server;

/// <summary>Where one dedicated server lives; absolute paths the host keeps in its settings.</summary>
/// <param name="CacheDir">PZ's user folder (<c>-cachedir=</c>), by default <c>~/Zomboid</c>.</param>
/// <param name="StateDir">Owned by the launcher. The running server's file in it holds the RCON password, so the host makes it owner-only.</param>
public sealed record ServerSite(string InstallRoot, string CacheDir, string StateDir);
