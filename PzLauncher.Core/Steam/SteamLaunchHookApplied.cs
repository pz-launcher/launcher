using PzLauncher.Core.Common;

namespace PzLauncher.Core.Steam;

/// <param name="Warnings">The change was written, but something after it failed (Steam didn't start again).</param>
public sealed record SteamLaunchHookApplied(SteamLaunchHookStatus Status, IReadOnlyList<Error> Warnings);
