namespace PzLauncher.Core.Mod.ZombieBuddy.Preload;

public enum ZombieBuddyPreload
{
    /// <summary>Unsupported install, unreadable game json, agent arg not allow-all, the mod not found, or no entry to remove.</summary>
    NotApplicable,

    /// <summary>ZombieBuddy's config already preloads this copy, or a byte-identical one, at the asked position.</summary>
    Current,

    Written,

    Removed,
}
