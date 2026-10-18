using PzLauncher.Core.Common;
using PzLauncher.Core.Mod.ZombieBuddy;
using PzLauncher.Desktop.Services.ZombieBuddy;

namespace PzLauncher.Desktop.Tests.TestSupport;

/// <summary>Initialize and remove run <see cref="Operation"/>, which by default reports every step and succeeds.</summary>
public sealed class FakeZombieBuddyService : IZombieBuddyService
{
    public Func<string, Result<ZombieBuddyStatus>> Status { get; set; } = _ => ZombieBuddyStatus.Unsupported;

    public Func<IProgress<ZombieBuddyProgress>?, CancellationToken, Task<Result>> Operation { get; set; } =
        (progress, _) => Task.FromResult(TestZombieBuddy.Run(TestZombieBuddy.InitializeSteps, progress));

    public Func<bool, Task<Result>> SetAllowAll { get; set; } = _ => Task.FromResult(Result.Ok());

    public List<string> StatusRequests { get; } = [];

    public List<(string Root, ZombieBuddyChannel Channel)> Initializations { get; } = [];

    public List<string> Removals { get; } = [];

    public List<(string Root, bool AllowAll)> AllowAllChanges { get; } = [];

    public Task<Result<ZombieBuddyStatus>> GetStatusAsync(string installRoot, CancellationToken ct = default)
    {
        StatusRequests.Add(installRoot);
        return Task.FromResult(Status(installRoot));
    }

    public Task<Result> InitializeAsync(
        string installRoot, ZombieBuddyChannel channel, IProgress<ZombieBuddyProgress>? progress = null, CancellationToken ct = default)
    {
        Initializations.Add((installRoot, channel));
        return Operation(progress, ct);
    }

    public Task<Result> RemoveAsync(string installRoot, IProgress<ZombieBuddyProgress>? progress = null, CancellationToken ct = default)
    {
        Removals.Add(installRoot);
        return Operation(progress, ct);
    }

    public Task<Result> SetAllowAllAsync(string installRoot, bool allowAll, CancellationToken ct = default)
    {
        AllowAllChanges.Add((installRoot, allowAll));
        return SetAllowAll(allowAll);
    }

    public Task<Result> EnsurePatchesJarAsync(
        string installRoot, string sourceJar, string jarFile, string packageName, CancellationToken ct = default) => Task.FromResult(Result.Ok());

    public Task<Result> RemovePatchesJarAsync(string installRoot, string jarFile, string packageName, CancellationToken ct = default) =>
        Task.FromResult(Result.Ok());
}

public sealed class FakeZombieBuddyModsService : IZombieBuddyModsService
{
    public Func<IReadOnlyList<Error>> Prepare { get; set; } = () => [];

    public List<(string Root, string ExtraGameArgs)> Prepares { get; } = [];

    public Task<IReadOnlyList<Error>> PrepareLaunchAsync(string installRoot, string extraGameArgs, CancellationToken ct = default)
    {
        Prepares.Add((installRoot, extraGameArgs));
        return Task.FromResult(Prepare());
    }
}

public sealed class FakeZombieBuddyChannelService : IZombieBuddyChannelService
{
    private readonly Dictionary<string, ZombieBuddyChannel> _channels = new(StringComparer.OrdinalIgnoreCase);

    public List<(string Root, ZombieBuddyChannel Channel)> Saved { get; } = [];

    public ZombieBuddyChannel Get(string installRoot) => _channels.GetValueOrDefault(installRoot, ZombieBuddyChannel.Stable);

    /// <summary>Arranges a saved value without recording a save.</summary>
    public void Set(string installRoot, ZombieBuddyChannel channel) => _channels[installRoot] = channel;

    public void Save(string installRoot, ZombieBuddyChannel channel)
    {
        _channels[installRoot] = channel;
        Saved.Add((installRoot, channel));
    }
}

public static class TestZombieBuddy
{
    public static readonly ZombieBuddyItem StableItem =
        new(ZombieBuddyChannel.Stable, @"E:\SteamLibrary\steamapps\workshop\content\108600\3619862853");

    public static readonly ZombieBuddyItem BetaItem =
        new(ZombieBuddyChannel.Beta, @"E:\SteamLibrary\steamapps\workshop\content\108600\3812624292");

    public static readonly ZombieBuddyStep[] InitializeSteps =
        [ZombieBuddyStep.FindItem, ZombieBuddyStep.CopyNativeLoader, ZombieBuddyStep.CopyAgentJar, ZombieBuddyStep.AddAgentArg];

    public static readonly ZombieBuddyStep[] RemoveSteps = [ZombieBuddyStep.DeleteFiles, ZombieBuddyStep.RemoveAgentArg];

    public const string ConfigFolder = @"C:\Users\u\.zombie_buddy";

    public static readonly ZombieBuddyStatus NotSubscribed = new(true, [], false, false, false, ConfigFolder, ZombieBuddyAction.None);

    public static readonly ZombieBuddyStatus Leftovers = new(true, [], false, true, false, ConfigFolder, ZombieBuddyAction.Remove);

    public static ZombieBuddyStatus NotInitialized(params ZombieBuddyItem[] subscribed) =>
        new(true, subscribed.Length == 0 ? [StableItem] : subscribed, false, false, false, ConfigFolder, ZombieBuddyAction.Initialize);

    public static ZombieBuddyStatus Initialized(bool allowAll = false, params ZombieBuddyItem[] subscribed) =>
        new(true, subscribed.Length == 0 ? [StableItem] : subscribed, true, true, allowAll, ConfigFolder, ZombieBuddyAction.None);

    /// <summary>Reports like the real service: each step as it starts, then completion; fails at <paramref name="failAt"/> with <paramref name="error"/>.</summary>
    public static Result Run(IReadOnlyList<ZombieBuddyStep> steps, IProgress<ZombieBuddyProgress>? progress, int? failAt = null, Error? error = null)
    {
        for (var i = 0; i < steps.Count; i++)
        {
            progress?.Report(new ZombieBuddyProgress(steps, i, IsComplete: false));
            if (i == failAt)
            {
                return error ?? TestErrors.Create(ErrorCode.ZombieBuddyCopyFailed, @"E:\SteamLibrary\steamapps\common\ProjectZomboid\ZombieBuddy.jar");
            }
        }

        progress?.Report(new ZombieBuddyProgress(steps, steps.Count, IsComplete: true));
        return Result.Ok();
    }
}
