using System.Threading.Tasks;
using PzLauncher.Core.Launch;
using PzLauncher.Desktop.Lang;
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
            AfterLaunchSettings.Options(),
            AfterLaunchAction.Keep);
        if (await dialogs.ChooseOneAsync(prompt) is not { } chosen)
        {
            return AfterLaunchAction.Keep;
        }

        settings.Action = chosen;
        return chosen;
    }

    /// <summary>Call once the game has started.</summary>
    public void Apply(AfterLaunchAction action)
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

    /// <summary>
    /// For <see cref="AfterLaunchAction.Close"/>: closes the launcher once the game has outlived its early-exit window, unless the user
    /// brought the window back meanwhile. <c>true</c> when the launcher is closing; an early exit is a failed start the user must see.
    /// </summary>
    public async Task<bool> CloseWhenStartedAsync(AfterLaunchAction action)
    {
        if (action != AfterLaunchAction.Close || !await launcher.WaitPastEarlyExitAsync() || !window.IsHidden)
        {
            return false;
        }

        lifetime.Close();
        return true;
    }

    /// <summary>Call when the game has exited, before any exit error shows.</summary>
    public void Restore(AfterLaunchAction action)
    {
        if (action != AfterLaunchAction.Keep)
        {
            window.Restore();
        }
    }
}
