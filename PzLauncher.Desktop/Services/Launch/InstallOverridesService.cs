using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PzLauncher.Core.Launch;
using PzLauncher.Core.Platform;
using PzLauncher.Desktop.Services.Preferences;

namespace PzLauncher.Desktop.Services.Launch;

public sealed class InstallOverridesService(IPreferenceStore store, UserPreferences saved, IPlatformEnvironment platform, TimeProvider time) : IInstallOverridesService
{
    private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(300);

    private readonly IPreferenceStore _store = store;
    private readonly TimeProvider _time = time;

    // Timer callbacks run on the thread pool.
    private readonly Lock _gate = new();
    private readonly InstallKeyedMap<GameLaunchOverrides> _overrides = new InstallKeyedMap<GameLaunchOverrides>(saved.InstallOverrides, platform.PathComparison);
    private readonly Dictionary<string, ITimer> _pendingWrites = new Dictionary<string, ITimer>(StringComparer.FromComparison(platform.PathComparison));
    private readonly List<Task> _writesInFlight = [];

    public GameLaunchOverrides Get(string installRoot)
    {
        lock (_gate)
        {
            return _overrides.GetValueOrDefault(installRoot) ?? GameLaunchOverrides.Empty;
        }
    }

    public void Save(string installRoot, GameLaunchOverrides overrides)
    {
        lock (_gate)
        {
            var key = _overrides.Set(installRoot, overrides);
            if (_pendingWrites.TryGetValue(key, out var timer))
            {
                timer.Change(SaveDelay, Timeout.InfiniteTimeSpan);
            }
            else
            {
                _pendingWrites[key] = _time.CreateTimer(_ => Write(key), null, SaveDelay, Timeout.InfiniteTimeSpan);
            }
        }
    }

    public Task FlushAsync(CancellationToken ct = default)
    {
        List<string> pending;
        lock (_gate)
        {
            pending = [.. _pendingWrites.Keys];
        }

        foreach (var key in pending)
        {
            Write(key);
        }

        lock (_gate)
        {
            return Task.WhenAll(_writesInFlight).WaitAsync(ct);
        }
    }

    private void Write(string key)
    {
        lock (_gate)
        {
            // A flush and the timer can both reach here; the first one writes.
            if (!_pendingWrites.Remove(key, out var timer))
            {
                return;
            }

            timer.Dispose();
            _writesInFlight.RemoveAll(write => write.IsCompleted);

            // SaveInstallOverridesAsync never throws for I/O, and a failed save must not undo the change.
            _writesInFlight.Add(_store.SaveInstallOverridesAsync(key, _overrides[key]));
        }
    }
}
