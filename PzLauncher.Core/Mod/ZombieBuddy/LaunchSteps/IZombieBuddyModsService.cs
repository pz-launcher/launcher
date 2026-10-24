using PzLauncher.Core.Common;

namespace PzLauncher.Core.Mod.ZombieBuddy.LaunchSteps;

/// <summary>Sets up the ZombieBuddy Java mods the launcher manages before a launch.</summary>
public interface IZombieBuddyModsService
{
    /// <summary>A failed step doesn't stop the later ones; the errors to show, in step order.</summary>
    Task<IReadOnlyList<Error>> PrepareLaunchAsync(string installRoot, string extraGameArgs, CancellationToken ct = default);
}
