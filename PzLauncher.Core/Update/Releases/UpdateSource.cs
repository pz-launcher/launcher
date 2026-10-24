namespace PzLauncher.Core.Update.Releases;

public enum UpdateSource
{
    /// <summary>Asks GitHub and Gitee at once and uses whichever answers first.</summary>
    Auto,

    GitHub,

    Gitee,
}
