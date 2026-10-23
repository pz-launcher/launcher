using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PzLauncher.Core.Common;
using PzLauncher.Core.Net;
using PzLauncher.Desktop.Services.Preferences.Records;
using PzLauncher.Desktop.Services.Preferences;

namespace PzLauncher.Desktop.Services.Net;

public sealed partial class ProxySettingsService : IProxySettingsService
{
    private static readonly TimeSpan ApplyDelay = TimeSpan.FromMilliseconds(300);

    private readonly INetworkService _network;
    private readonly IPreferenceStore _store;
    private readonly TimeProvider _time;

    // Timer callbacks run on the thread pool.
    private readonly Lock _gate = new();
    private readonly List<Task> _writesInFlight = [];
    private SavedProxy _current;
    private SavedProxy? _pending;
    private ITimer? _pendingTimer;

    /// <summary>Applies the saved choice, so construct it before the first network request.</summary>
    public ProxySettingsService(INetworkService network, IPreferenceStore store, UserPreferences saved, TimeProvider time, ILogger<ProxySettingsService> logger)
    {
        _network = network;
        _store = store;
        _time = time;
        _current = saved.Proxy ?? SavedProxy.Default;
        var applied = network.ApplyProxy(_current.ToSettings());
        if (!applied.IsSuccess)
        {
            // The store drops invalid fields, so a manual choice can arrive without a host or port.
            LogSavedProxyInvalid(logger, applied.Error.Code.ToWireName());
            _current = _current with { Mode = ProxyMode.System };
            network.ApplyProxy(ProxySettings.Default);
        }
    }

    public SavedProxy Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    public Result Apply(SavedProxy proxy)
    {
        var validated = ProxySettingsValidation.Validate(proxy.ToSettings());
        lock (_gate)
        {
            CancelPending();
            if (!validated.IsSuccess)
            {
                return validated.Error;
            }

            ApplyNow(proxy);
            return Result.Ok();
        }
    }

    public Result ApplyDebounced(SavedProxy proxy)
    {
        var validated = ProxySettingsValidation.Validate(proxy.ToSettings());
        lock (_gate)
        {
            if (!validated.IsSuccess)
            {
                CancelPending();
                return validated.Error;
            }

            _pending = proxy;
            if (_pendingTimer is { } timer)
            {
                timer.Change(ApplyDelay, Timeout.InfiniteTimeSpan);
            }
            else
            {
                _pendingTimer = _time.CreateTimer(_ => ApplyPending(), null, ApplyDelay, Timeout.InfiniteTimeSpan);
            }

            return Result.Ok();
        }
    }

    public Task FlushAsync(CancellationToken ct = default)
    {
        ApplyPending();
        lock (_gate)
        {
            return Task.WhenAll(_writesInFlight).WaitAsync(ct);
        }
    }

    private void ApplyPending()
    {
        lock (_gate)
        {
            // A flush and the timer can both reach here; the first one applies.
            if (_pending is not { } pending)
            {
                return;
            }

            CancelPending();
            ApplyNow(pending);
        }
    }

    private void CancelPending()
    {
        _pending = null;
        _pendingTimer?.Dispose();
        _pendingTimer = null;
    }

    private void ApplyNow(SavedProxy proxy)
    {
        // Validated by the caller, so this can't fail.
        _network.ApplyProxy(proxy.ToSettings());
        _current = proxy;
        _writesInFlight.RemoveAll(write => write.IsCompleted);

        // SaveProxyAsync never throws for I/O, and a failed save must not undo the change.
        _writesInFlight.Add(_store.SaveProxyAsync(proxy));
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Saved proxy settings are not valid ({Code}); using the system proxy")]
    private static partial void LogSavedProxyInvalid(ILogger logger, string code);
}
