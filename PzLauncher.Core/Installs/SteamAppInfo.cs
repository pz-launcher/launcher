namespace PzLauncher.Core.Installs;

/// <param name="Branch"><c>null</c> on the default (public) branch.</param>
public sealed record SteamAppInfo(string BuildId, string? Branch);
