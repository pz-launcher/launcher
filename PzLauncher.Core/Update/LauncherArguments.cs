namespace PzLauncher.Core.Update;

/// <summary>Command-line arguments the launcher passes to itself; public so the host can read them.</summary>
public static class LauncherArguments
{
    /// <summary>Started by a self-update: the old instance is still closing, so wait for it instead of handing over to it.</summary>
    public const string Restart = "--pzlauncher-restart";

    /// <summary>First argument: run as the Steam helper (<c>SteamHelperHost</c>) instead of the app.</summary>
    public const string SteamHelper = "--pzlauncher-steam-helper";
}
