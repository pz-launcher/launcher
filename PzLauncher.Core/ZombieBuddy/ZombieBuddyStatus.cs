namespace PzLauncher.Core.ZombieBuddy;

/// <param name="IsSupported">A game install on Windows or Linux; otherwise every other member is empty or false.</param>
/// <param name="Subscribed">Stable first.</param>
/// <param name="IsInitialized">Every required file is in the game folder and the json's top-level <c>vmArgs</c> has the agent arg.</param>
/// <param name="HasLeftovers">Any ZombieBuddy file in the game folder, or any agent arg in the json's top-level <c>vmArgs</c>.</param>
/// <param name="IsAllowAll">The json's agent arg carries <c>policy=allow-all</c>.</param>
public sealed record ZombieBuddyStatus(
    bool IsSupported,
    IReadOnlyList<ZombieBuddyItem> Subscribed,
    bool IsInitialized,
    bool HasLeftovers,
    bool IsAllowAll,
    ZombieBuddyAction NeededAction)
{
    public static ZombieBuddyStatus Unsupported { get; } = new(false, [], false, false, false, ZombieBuddyAction.None);
}
