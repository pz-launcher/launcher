namespace PzLauncher.Core.Platform;

/// <summary>The only way Core starts processes, so tests can fake them.</summary>
internal interface IProcessRunner
{
    /// <summary>Throws the OS's exception (<see cref="System.ComponentModel.Win32Exception"/>, <see cref="IOException"/>, <see cref="UnauthorizedAccessException"/>) when the process can't start.</summary>
    IRunningProcess Start(ProcessStartRequest request);

    /// <summary>Like <see cref="Start"/>, with stdin and stdout redirected to the caller.</summary>
    IPipedProcess StartPiped(ProcessStartRequest request);

    /// <summary>Processes of the current session running an executable with this name (no extension); their stderr isn't read.</summary>
    IReadOnlyList<IRunningProcess> FindByName(string name);

    /// <summary>Like <see cref="Start"/>, but std handles on the null device, in its own process group, and outliving the caller.</summary>
    DetachedProcess StartDetached(ProcessStartRequest request);

    /// <summary><c>null</c> when no process has this id, or it can't be read.</summary>
    ProcessInfo? GetById(int id);

    /// <summary>Kills the process and its children; does nothing once it is gone. Throws <see cref="System.ComponentModel.Win32Exception"/> when the OS refuses.</summary>
    void KillTree(int id);
}
