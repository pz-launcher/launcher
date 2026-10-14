namespace PzLauncher.Core.Common;

public enum ErrorKind
{
    /// <summary>The caller passed bad input.</summary>
    Validation,

    NotFound,

    /// <summary>The current state doesn't allow the operation.</summary>
    Conflict,

    /// <summary>Steam files, an OS call, a child process, or the network failed.</summary>
    External,

    /// <summary>Caught at a boundary but not classified.</summary>
    Unexpected,
}
