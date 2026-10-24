namespace PzLauncher.Core.Server;

public enum ServerState
{
    Stopped,

    /// <summary>The process runs, but RCON hasn't answered yet.</summary>
    Starting,
    Running,

    /// <summary>Asked to quit or killed; not exited yet.</summary>
    Stopping,
}
