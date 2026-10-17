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

    /// <summary>
    /// Copies <paramref name="sourceJar"/> to the game folder as <paramref name="jarFile"/> when missing or different, then makes
    /// the agent arg's <c>patches_jar</c> hold one <c>&lt;jarFile&gt;:&lt;packageName&gt;</c> entry for the package. Without an
    /// agent arg nothing is done; nothing is written when both are already so.
    /// </summary>
    Task<Result> EnsurePatchesJarAsync(string installRoot, string sourceJar, string jarFile, string packageName, CancellationToken ct = default);

    /// <summary>Removes the package's <c>patches_jar</c> entries, then deletes <paramref name="jarFile"/> from the game folder; nothing is written when neither exists.</summary>
    Task<Result> RemovePatchesJarAsync(string installRoot, string jarFile, string packageName, CancellationToken ct = default);
}
