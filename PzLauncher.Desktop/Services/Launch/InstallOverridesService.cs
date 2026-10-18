using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PzLauncher.Core.Launch;
using PzLauncher.Core.Platform;
using PzLauncher.Desktop.Services.Preferences;

namespace PzLauncher.Desktop.Services.Launch;

public sealed class InstallOverridesService : IInstallOverridesService
{
    private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(300);

    private readonly IPreferenceStore _store;
    private readonly TimeProvider _time;

    // Timer callbacks run on the thread pool.
    private readonly Lock _gate = new();
    private readonly Dictionary<string, GameLaunchOverrides> _overrides;
    private readonly Dictionary<string, ITimer> _pendingWrites;
    private readonly List<Task> _writesInFlight = [];

    public InstallOverridesService(IPreferenceStore store, UserPreferences saved, IPlatformEnvironment platform, TimeProvider time)
    {
        _store = store;
        _time = time;
        var comparer = StringComparer.FromComparison(platform.PathComparison);
        _overrides = new Dictionary<string, GameLaunchOverrides>(comparer);
        foreach (var (root, overrides) in saved.InstallOverrides)
        {
            _overrides.TryAdd(root, overrides);
        }

        _pendingWrites = new Dictionary<string, ITimer>(comparer);
    }

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
            // Write under the saved spelling of the path, so a differently cased root replaces its entry instead of adding one.
            var key = _overrides.Keys.FirstOrDefault(root => _overrides.Comparer.Equals(root, installRoot)) ?? installRoot;
            _overrides[key] = overrides;
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
