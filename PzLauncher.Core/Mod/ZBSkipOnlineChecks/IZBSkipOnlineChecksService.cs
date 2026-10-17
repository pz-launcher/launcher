using PzLauncher.Core.Common;

namespace PzLauncher.Core.Mod.ZBSkipOnlineChecks;

/// <summary>Keeps ZB Skip Online Checks' preload entry and agent start in step with the game's enabled mod list.</summary>
public interface IZBSkipOnlineChecksService
{
    /// <summary>
    /// Sets up both start ways when the enabled list has the mod and allow-all is on, removes them when it lacks it.
    /// The preload entry is only written when <paramref name="zabricPreloaded"/>: the mod's preLaunch runs from Zabric.
    /// </summary>
    Task<Result> SyncAsync(string installRoot, string extraGameArgs, bool zabricPreloaded, CancellationToken ct = default);
}
