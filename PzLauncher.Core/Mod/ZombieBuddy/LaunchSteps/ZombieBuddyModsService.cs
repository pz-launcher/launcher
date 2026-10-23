using PzLauncher.Core.Common;
using PzLauncher.Core.Platform;
using PzLauncher.Core.Steam;

namespace PzLauncher.Core.Mod.ZombieBuddy.LaunchSteps;

internal sealed class ZombieBuddyModsService(
    IZombieBuddyService zombieBuddy,
    WorkshopLibraries workshop,
    IPlatformEnvironment platform,
    IEnumerable<IZombieBuddyLaunchStep> steps) : IZombieBuddyModsService
{
    // Concurrent launches would otherwise interleave their config.json and game json rewrites.
    private readonly SemaphoreSlim _gate = new(1, 1);

    // Registration order: a step may read what an earlier one preloaded.
    private readonly IZombieBuddyLaunchStep[] _steps = [.. steps];

    public async Task<IReadOnlyList<Error>> PrepareLaunchAsync(string installRoot, string extraGameArgs, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var launch = await ZombieBuddyLaunch.ResolveAsync(zombieBuddy, workshop, platform, installRoot, extraGameArgs, ct).ConfigureAwait(false);
            if (launch is null)
            {
                return [];
            }

            var errors = new List<Error>();
            foreach (var step in _steps)
            {
                var result = await step.RunAsync(launch, ct).ConfigureAwait(false);
                if (!result.IsSuccess)
                {
                    errors.Add(result.Error);
                }
            }

            return errors;
        }
        finally
        {
            _gate.Release();
        }
    }
}
