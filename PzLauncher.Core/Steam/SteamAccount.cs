namespace PzLauncher.Core.Steam;

/// <param name="AccountId">The low 32 bits of the SteamID64; names the account's <c>userdata</c> folder.</param>
/// <param name="PersonaName">The profile name Steam shows; the login name or the id when Steam saved none.</param>
public sealed record SteamAccount(uint AccountId, string PersonaName);
