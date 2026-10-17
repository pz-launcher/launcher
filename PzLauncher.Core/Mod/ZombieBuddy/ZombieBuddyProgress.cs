namespace PzLauncher.Core.Mod.ZombieBuddy;

/// <param name="Steps">Every step of the operation, in order.</param>
/// <param name="Current">Index of the running step; <c>Steps.Count</c> once complete.</param>
public sealed record ZombieBuddyProgress(IReadOnlyList<ZombieBuddyStep> Steps, int Current, bool IsComplete);
