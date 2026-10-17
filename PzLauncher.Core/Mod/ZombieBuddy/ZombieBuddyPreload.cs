namespace PzLauncher.Core.Mod.ZombieBuddy;

public enum ZombieBuddyPreload
{
    /// <summary>Unsupported install, unreadable game json, agent arg not allow-all, or the mod not found.</summary>
    NotApplicable,

    /// <summary>ZombieBuddy's config already preloads this copy, or a byte-identical one, at the asked position.</summary>
    Current,

    Written,
}
