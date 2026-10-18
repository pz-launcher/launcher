namespace PzLauncher.Core.Steam;

public enum SteamLaunchHookState
{
    Off,

    /// <summary>Steam starts this launcher.</summary>
    On,

    /// <summary>Steam starts a PzLauncher in another folder; counts as off for this one.</summary>
    OtherLauncher,
}
