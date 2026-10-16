namespace PzLauncher.Core.ZombieBuddy;

/// <summary>What must run before the game starts.</summary>
public enum ZombieBuddyAction
{
    None,

    /// <summary>Subscribed but not initialized.</summary>
    Initialize,

    /// <summary>Not subscribed, but files or an agent arg are left behind.</summary>
    Remove,
}
