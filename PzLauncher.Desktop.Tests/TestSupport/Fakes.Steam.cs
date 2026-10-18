using PzLauncher.Core.Common;
using PzLauncher.Core.Steam;
using PzLauncher.Desktop.Localization;
using PzLauncher.Desktop.Services.Preferences;
using PzLauncher.Desktop.Services.Steam;
using PzLauncher.Desktop.ViewModels.Settings;

namespace PzLauncher.Desktop.Tests.TestSupport;

/// <summary>Holds Steam's state in <see cref="Current"/>: a successful set changes it as the real service changes the file.</summary>
public sealed class FakeSteamLaunchHookService : ISteamLaunchHookService
{
    public SteamLaunchHookStatus Current { get; set; } = TestSteam.Status(SteamLaunchHookState.Off);

    /// <summary>Returned by every status read instead of <see cref="Current"/>.</summary>
    public Error? StatusError { get; set; }

    /// <summary>Returned by a set instead of changing <see cref="Current"/>.</summary>
    public Error? SetError { get; set; }

    public IReadOnlyList<Error> SetWarnings { get; set; } = [];

    /// <summary>Holds every set open, after recording it, until completed.</summary>
    public TaskCompletionSource? SetGate { get; set; }

    public List<string> StatusRequests { get; } = [];

    public List<(string LauncherExe, bool Enabled, bool RestartSteam)> Sets { get; } = [];

    public Task<Result<SteamLaunchHookStatus>> GetStatusAsync(string launcherExe, CancellationToken ct = default)
    {
        StatusRequests.Add(launcherExe);
        return Task.FromResult(StatusError is { } error ? Result<SteamLaunchHookStatus>.Fail(error) : Current);
    }

    public async Task<Result<SteamLaunchHookApplied>> SetAsync(string launcherExe, bool enabled, bool restartSteam, CancellationToken ct = default)
    {
        Sets.Add((launcherExe, enabled, restartSteam));
        if (SetGate is { } gate)
        {
            await gate.Task.WaitAsync(ct);
        }

        if (SetError is { } error)
        {
            return error;
        }

        Current = Current with { State = enabled ? SteamLaunchHookState.On : SteamLaunchHookState.Off, HookedPath = null };
        return new SteamLaunchHookApplied(Current, SetWarnings);
    }
}

public static class TestSteam
{
    public const string LauncherExe = @"C:\Games\PzLauncher\PzLauncher.exe";

    public const string OtherLauncherExe = @"D:\Old\PzLauncher.exe";

    public static readonly SteamAccount Account = new(12345678, "Survivor");

    public static SteamLaunchHookStatus Status(SteamLaunchHookState state, bool isSteamRunning = false) =>
        new(Account, state, state == SteamLaunchHookState.OtherLauncher ? OtherLauncherExe : null, isSteamRunning);

    public static SteamLaunchHookFlow Flow(FakeSteamLaunchHookService hooks, FakeDialogService dialogs) => new(hooks, dialogs, LauncherExe);

    public static SteamSettingsViewModel ViewModel(ILocalizationService localization, IPreferenceStore store, FakeSteamLaunchHookService? hooks = null)
    {
        var dialogs = new FakeDialogService();
        return new SteamSettingsViewModel(
            Flow(hooks ?? new FakeSteamLaunchHookService(), dialogs), store, new UserPreferences(null, null, null), dialogs, new FakePlatformEnvironment(), localization);
    }
}
