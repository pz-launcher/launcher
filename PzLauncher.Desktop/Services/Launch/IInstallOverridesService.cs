using System.Threading;
using System.Threading.Tasks;
using PzLauncher.Core.Launch;

namespace PzLauncher.Desktop.Services.Launch;

/// <summary>Per-install launch overrides for the session, looked up with the platform's path comparison and persisted through <see cref="IPreferenceStore"/>.</summary>
public interface IInstallOverridesService
{
    /// <summary><see cref="GameLaunchOverrides.Empty"/> when none are saved.</summary>
    GameLaunchOverrides Get(string installRoot);

    /// <summary>Applies at once; the file write is fire-and-forget and coalesced, so a slider drag doesn't write per step.</summary>
    void Save(string installRoot, GameLaunchOverrides overrides);

    /// <summary>Writes pending changes now and completes when every write has finished; called at exit.</summary>
    Task FlushAsync(CancellationToken ct = default);
}
