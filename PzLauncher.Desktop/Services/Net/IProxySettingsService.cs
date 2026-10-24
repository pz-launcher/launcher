using System.Threading;
using System.Threading.Tasks;
using PzLauncher.Core.Common;
using PzLauncher.Desktop.Services.Preferences.Records;
using PzLauncher.Desktop.Services.Preferences;

namespace PzLauncher.Desktop.Services.Net;

/// <summary>The proxy choice for the session: applied through Core's network service and persisted through <see cref="IPreferenceStore"/>.</summary>
public interface IProxySettingsService
{
    /// <summary>The choice in effect, with the manual fields as last saved.</summary>
    SavedProxy Current { get; }

    /// <summary>Validates; on success applies and saves at once. On failure nothing changes and a pending change is dropped.</summary>
    Result Apply(SavedProxy proxy);

    /// <summary>Validates at once; on success applies and saves after a short delay, so typing doesn't switch the proxy per keystroke.</summary>
    Result ApplyDebounced(SavedProxy proxy);

    /// <summary>Applies a pending change now and completes when every write has finished; called at exit and before a connection test.</summary>
    Task FlushAsync(CancellationToken ct = default);
}
