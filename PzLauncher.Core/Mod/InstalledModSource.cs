namespace PzLauncher.Core.Mod;

public enum InstalledModSource
{
    /// <summary>A Workshop item uploaded from this machine, under <c>&lt;cache&gt;/Workshop</c>.</summary>
    StagedWorkshop,

    SteamWorkshop,

    /// <summary><c>&lt;cache&gt;/mods</c>.</summary>
    Local,
}
