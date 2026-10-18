namespace PzLauncher.Core.Steam;

/// <param name="Account">The current Steam account, whose launch options were read.</param>
/// <param name="HookedPath">The other launcher's path with <see cref="SteamLaunchHookState.OtherLauncher"/>; <c>null</c> otherwise.</param>
public sealed record SteamLaunchHookStatus(SteamAccount Account, SteamLaunchHookState State, string? HookedPath, bool IsSteamRunning);
