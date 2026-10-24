using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PzLauncher.Core.Common;
using PzLauncher.Core.Steam.LaunchHook;
using PzLauncher.Core.Steam;
using PzLauncher.Desktop.Lang;
using PzLauncher.Desktop.Localization;
using PzLauncher.Desktop.Services.Shell.Dialogs;
using PzLauncher.Desktop.Services.Shell;

namespace PzLauncher.Desktop.Services.Steam;

/// <summary>Turns Steam's launch hook on or off for the running launcher, shared by Settings and the startup reminder.</summary>
/// <param name="launcherExe">The running launcher's absolute path, which the hook points Steam to.</param>
public sealed class SteamLaunchHookFlow(ISteamLaunchHookService hooks, IDialogService dialogs, string launcherExe)
{
    public Task<Result<SteamLaunchHookStatus>> GetStatusAsync(CancellationToken ct = default) => hooks.GetStatusAsync(launcherExe, ct);

    /// <summary>
    /// Asks before closing a running Steam and shows what went wrong. Returns the state Steam holds afterwards, which is the old
    /// one when the user cancels or the change fails; a failure only when that state can't be read.
    /// </summary>
    public async Task<Result<SteamLaunchHookStatus>> SetAsync(bool enabled)
    {
        var status = await hooks.GetStatusAsync(launcherExe);
        if (!status.IsSuccess)
        {
            await dialogs.ShowErrorAsync(ErrorMessages.ToUserText(status.Error));
            return status;
        }

        var restartSteam = status.Value.IsSteamRunning;
        if (restartSteam && !await dialogs.ConfirmAsync(new ConfirmPrompt(
                UiText.Steam_ConfirmTitle, UiText.Steam_ConfirmMessage, UiText.Steam_ConfirmApply, UiText.Dialog_Cancel)))
        {
            return status;
        }

        var applied = await hooks.SetAsync(launcherExe, enabled, restartSteam);
        if (!applied.IsSuccess)
        {
            await dialogs.ShowErrorAsync(ErrorMessages.ToUserText(applied.Error));
            return await hooks.GetStatusAsync(launcherExe);
        }

        if (applied.Value.Warnings.Count > 0)
        {
            await dialogs.ShowErrorAsync(string.Join(Environment.NewLine, applied.Value.Warnings.Select(ErrorMessages.ToUserText)));
        }

        return applied.Value.Status;
    }
}
