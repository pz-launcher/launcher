namespace PzLauncher.Core.Platform;

/// <summary>The only way Core starts processes, so tests can fake them.</summary>
internal interface IProcessRunner
{
    /// <summary>Throws the OS's exception (<see cref="System.ComponentModel.Win32Exception"/>, <see cref="IOException"/>, <see cref="UnauthorizedAccessException"/>) when the process can't start.</summary>
    IRunningProcess Start(ProcessStartRequest request);

    /// <summary>Processes of the current session running an executable with this name (no extension); their stderr isn't read.</summary>
    IReadOnlyList<IRunningProcess> FindByName(string name);
}
