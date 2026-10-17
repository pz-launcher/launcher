namespace PzLauncher.Core.Zabric;

public enum ZabricPreload
{
    /// <summary>Unsupported install, unreadable game json, agent arg not allow-all, or no Zabric found.</summary>
    NotApplicable,

    /// <summary>ZombieBuddy's config already preloads this copy, or a byte-identical one, first.</summary>
    Current,

    Written,
}
