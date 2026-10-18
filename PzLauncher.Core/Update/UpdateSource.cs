namespace PzLauncher.Core.Update;

public enum UpdateSource
{
    /// <summary>Asks GitHub and Gitee at once and uses whichever answers first.</summary>
    Auto,

    GitHub,

    Gitee,
}
