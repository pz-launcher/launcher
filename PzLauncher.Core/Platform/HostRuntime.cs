using System.Runtime.InteropServices;

namespace PzLauncher.Core.Platform;

/// <summary>Facts about the running process rather than the target OS; services take them as <c>internal init</c> defaults.</summary>
internal static class HostRuntime
{
    public static Architecture ProcessArchitecture => RuntimeInformation.ProcessArchitecture;

    // The native ABI of this process (e.g. Steam's callback struct packing), not the OS a service targets.
    public static bool IsWindows => OperatingSystem.IsWindows();

    // Decided by the host OS: tests fake Linux on Windows, where file modes don't exist.
    public static void EnsureUserExecutable(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        File.SetUnixFileMode(path, File.GetUnixFileMode(path) | UnixFileMode.UserExecute);
    }
}
