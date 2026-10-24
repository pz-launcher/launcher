using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace PzLauncher.Desktop.Services.Shell.Lifetime;

/// <summary>
/// One launcher per install folder on Windows: a second start (a double-click, Steam's Play button) brings the running one to
/// the front and exits. Launchers in other folders run side by side. Uses no Avalonia types: it runs before Avalonia starts.
/// </summary>
public sealed partial class SingleInstanceGuard : IDisposable
{
    private const int AsfwAny = -1;

    // The old instance's close flush is bounded, so this is too.
    private static readonly TimeSpan RestartWait = TimeSpan.FromMinutes(2);

    private readonly Mutex? _mutex;
    private readonly EventWaitHandle? _activate;
    private readonly RegisteredWaitHandle? _registration;

    private SingleInstanceGuard(Mutex? mutex, EventWaitHandle? activate)
    {
        _mutex = mutex;
        _activate = activate;
        if (activate is not null)
        {
            _registration = ThreadPool.RegisterWaitForSingleObject(
                activate, (_, _) => ActivationRequested?.Invoke(this, EventArgs.Empty), null, Timeout.Infinite, executeOnlyOnce: false);
        }
    }

    /// <summary>Raised on a thread-pool thread when another start of this launcher asks this one to come to the front.</summary>
    public event EventHandler? ActivationRequested;

    /// <param name="isRestart">Started by a self-update: wait for the closing old instance instead of handing over to it.</param>
    /// <returns><c>null</c> when this folder's launcher already runs and was asked to come to the front: exit.</returns>
    public static SingleInstanceGuard? Acquire(string baseDirectory, bool isRestart)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new SingleInstanceGuard(null, null);
        }

        var key = Key(baseDirectory);
        Mutex? mutex = null;
        EventWaitHandle activate;
        try
        {
            mutex = new Mutex(initiallyOwned: false, $@"Local\{key}");
            activate = new EventWaitHandle(false, EventResetMode.AutoReset, $@"Local\{key}-activate");
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or WaitHandleCannotBeOpenedException or IOException)
        {
            // Created by an instance at another integrity level (run as administrator): running unguarded beats not starting.
            mutex?.Dispose();
            return new SingleInstanceGuard(null, null);
        }

        if (TryOwn(mutex, isRestart ? RestartWait : TimeSpan.Zero))
        {
            return new SingleInstanceGuard(mutex, activate);
        }

        mutex.Dispose();
        if (isRestart)
        {
            // The old instance hangs on its way out; running unguarded beats not restarting at all.
            activate.Dispose();
            return new SingleInstanceGuard(null, null);
        }

        // This process may take the foreground (the user just started it); it lets the running one take it instead.
        AllowSetForegroundWindow(AsfwAny);
        activate.Set();
        activate.Dispose();
        return null;
    }

    /// <summary>Call on the thread that acquired it: a mutex is released by its owning thread.</summary>
    public void Dispose()
    {
        _registration?.Unregister(null);
        if (_mutex is not null)
        {
            _mutex.ReleaseMutex();
            _mutex.Dispose();
        }

        _activate?.Dispose();
    }

    private static bool TryOwn(Mutex mutex, TimeSpan wait)
    {
        try
        {
            return mutex.WaitOne(wait);
        }
        catch (AbandonedMutexException)
        {
            // A crashed instance left it; the OS hands ownership to this one.
            return true;
        }
    }

    // Per folder and session-local; the name must be a valid kernel object name, so the path is hashed.
    private static string Key(string baseDirectory)
    {
        var folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(baseDirectory)).ToUpperInvariant();
        return "PzLauncher-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(folder)))[..16];
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AllowSetForegroundWindow(int processId);
}
