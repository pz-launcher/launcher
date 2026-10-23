namespace PzLauncher.Core.Server;

public enum ServerConsoleLineKind
{
    Output,

    /// <summary>The file started over: a new server start, or its size limit mid-run. Lines written just before may be missing.</summary>
    FileRestarted,
}
