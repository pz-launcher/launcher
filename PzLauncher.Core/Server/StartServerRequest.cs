namespace PzLauncher.Core.Server;

/// <param name="AdminPassword">Required while the server has no database; given later, it resets the in-game admin's password. Never stored.</param>
/// <param name="MaxMemoryMb"><c>null</c>: keep the json's <c>-Xmx</c>.</param>
public sealed record StartServerRequest(ServerSite Site, string Name, string? AdminPassword, int? MaxMemoryMb);
