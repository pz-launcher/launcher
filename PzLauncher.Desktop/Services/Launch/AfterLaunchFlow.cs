using System.Threading.Tasks;
using PzLauncher.Core.Common;
using PzLauncher.Core.Launch;
using PzLauncher.Desktop.Lang;
using PzLauncher.Desktop.Services.Shell.Dialogs;
using PzLauncher.Desktop.Services.Shell.Lifetime;
using PzLauncher.Desktop.Services.Shell;

namespace PzLauncher.Desktop.Services.Launch;

/// <summary>What the launcher window does around a game session, by the user's <see cref="AfterLaunchAction"/>.</summary>
public sealed class AfterLaunchFlow(
    AfterLaunchSettings settings, ILauncherWindow window, IAppLifetime lifetime, IDialogService dialogs, IGameLaunchService launcher)
{
    /// <summary>The saved choice, else the user's answer, which is saved. Dismissed: <see cref="AfterLaunchAction.Keep"/> this time, and the next launch asks again.</summary>
    public async Task<AfterLaunchAction> ChooseAsync()
    {
        if (settings.Action is { } saved)
        {
            return saved;
        }

        var prompt = new ChoicePrompt<AfterLaunchAction>(
            UiText.AfterLaunch_PromptTitle,
            UiText.AfterLaunch_PromptMessage,
            UiText.AfterLaunch_PromptHint,
            settings.Options(),
            AfterLaunchAction.Keep);
        if (await dialogs.ChooseOneAsync(prompt) is not { } chosen)
        {
            return AfterLaunchAction.Keep;
        }

        settings.Action = chosen;
        return chosen;
    }

    /// <summary>
    /// Call once the game has started; completes when it exits, with its exit, or <c>null</c> when the launcher is closing.
    /// Still waits for the exit when closing, so the caller's run state stays set while the close flush runs.
    /// </summary>
    public async Task<Result?> WatchAsync(AfterLaunchAction action)
    {
        Apply(action);
        var closing = await CloseWhenStartedAsync(action);
        var exit = await launcher.WaitForExitAsync();
        if (closing)
        {
            return null;
        }

        // Before the caller shows an exit error, so it never shows over a hidden or minimized window.
        if (action != AfterLaunchAction.Keep)
        {
            window.Restore();
        }

        return exit;
    }

    private void Apply(AfterLaunchAction action)
    {
        switch (action)
        {
            case AfterLaunchAction.Keep:
                break;
            case AfterLaunchAction.Minimize:
                window.Minimize();
                break;
            case AfterLaunchAction.MinimizeToTray:
            case AfterLaunchAction.Close:
                window.Hide();
                break;
        }
    }

    // Closes once the game has outlived its early-exit window, unless the user brought the window back meanwhile;
    // an early exit is a failed start the user must see.
    private async Task<bool> CloseWhenStartedAsync(AfterLaunchAction action)
    {
        if (action != AfterLaunchAction.Close || !await launcher.WaitPastEarlyExitAsync() || !window.IsHidden)
        {
            return false;
        }

        lifetime.Close();
        return true;
    }
}
