using PzLauncher.Core.Common;

namespace PzLauncher.Core.Mod.ZombieBuddy;

/// <summary>One ZombieBuddy Java mod's setup for a launch; logs its own failures.</summary>
internal interface IZombieBuddyLaunchStep
{
    Task<Result> RunAsync(ZombieBuddyLaunch launch, CancellationToken ct);
}
