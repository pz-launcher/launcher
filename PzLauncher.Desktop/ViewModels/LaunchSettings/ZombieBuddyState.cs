namespace PzLauncher.Desktop.ViewModels.LaunchSettings;

public enum ZombieBuddyState
{
    NotInitialized,
    Initialized,

    /// <summary>Not subscribed, but files or an agent arg are left behind.</summary>
    Leftovers,
}
