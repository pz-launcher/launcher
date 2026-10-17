using PzLauncher.Core.Common;

namespace PzLauncher.Core.Mod.ZombieBuddy;

/// <summary>Sets up the ZombieBuddy Workshop mod in a game install: its files in the game folder, its agent arg in <c>ProjectZomboid64.json</c>.</summary>
public interface IZombieBuddyService
{
    /// <summary>Missing or unreadable Steam data means "not subscribed"; a server or macOS gives an unsupported status.</summary>
    Task<Result<ZombieBuddyStatus>> GetStatusAsync(string installRoot, CancellationToken ct = default);

    /// <summary>Idempotent: copies the files again (overwriting) and keeps an existing agent arg with its options.</summary>
    Task<Result> InitializeAsync(
        string installRoot, ZombieBuddyChannel channel, IProgress<ZombieBuddyProgress>? progress = null, CancellationToken ct = default);

    /// <summary>Idempotent: deletes the leftover files and every agent arg in the json's top-level <c>vmArgs</c>.</summary>
    Task<Result> RemoveAsync(string installRoot, IProgress<ZombieBuddyProgress>? progress = null, CancellationToken ct = default);

    /// <summary>Rewrites only the <c>policy</c> option of the json's agent arg.</summary>
    Task<Result> SetAllowAllAsync(string installRoot, bool allowAll, CancellationToken ct = default);
}
