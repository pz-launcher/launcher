using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using PzLauncher.Core.Mod.Modpacks;
using PzLauncher.Desktop.Lang;
using PzLauncher.Desktop.Services.Mods.Modpacks;
using PzLauncher.Desktop.Services.Shell;
using PzLauncher.Desktop.Services.Shell.Dialogs;

namespace PzLauncher.Desktop.ViewModels.Mods.Modpacks;

/// <summary>The modpack name rule as dialog text, the name dialog, and the question before deleting a source modpacks use.</summary>
public static class ModpackPrompts
{
    /// <summary><see cref="ModpackNames"/>' rule, which local collection names share, then a clash, as dialog text.</summary>
    public static string? NameError(string text, Func<string, bool> isTaken, string invalid, string taken) =>
        !ModpackNames.IsValid(text) ? invalid
        : isTaken(text) ? taken
        : null;

    /// <param name="except">The modpack being renamed or edited, which its own name doesn't clash with.</param>
    public static string? ValidateName(ModpackStore modpacks, string installRoot, string text, string? except = null) =>
        NameError(text, name => modpacks.IsNameTaken(installRoot, name, except), UiText.Modpacks_NameInvalid, UiText.Modpacks_NameTaken);

    /// <summary>The trimmed name; <c>null</c> when cancelled.</summary>
    /// <param name="except"><inheritdoc cref="ValidateName" path="/param[@name='except']"/></param>
    public static Task<string?> AskNameAsync(
        IDialogService dialogs, ModpackStore modpacks, string installRoot, string title, string initial, string accept, string? except = null) =>
        AskTrimmedAsync(
            dialogs, new TextPrompt(title, UiText.Modpacks_NameMessage, initial, accept, text => ValidateName(modpacks, installRoot, text, except)));

    /// <summary>The accepted text, trimmed; <c>null</c> when cancelled.</summary>
    public static async Task<string?> AskTrimmedAsync(IDialogService dialogs, TextPrompt prompt) =>
        await dialogs.PromptTextAsync(prompt) is TextPromptResult.Accepted accepted ? accepted.Text.Trim() : null;

    /// <summary>
    /// Before deleting a mod preset or local collection: when modpacks use it, <see cref="ConfirmChoice.Confirm"/> deletes it and leaves the
    /// modpacks their copy, <see cref="ConfirmChoice.Alternative"/> deletes them too; otherwise a plain confirm.
    /// </summary>
    /// <param name="users">The modpacks' names, as shown.</param>
    public static async Task<ConfirmChoice> ConfirmDeleteAsync(
        IDialogService dialogs, string title, string plainMessage, string usedMessage, IReadOnlyList<string> users)
    {
        if (users.Count > 0)
        {
            return await dialogs.ChooseAsync(
                new ConfirmPrompt(title, usedMessage, UiText.Modpacks_DeleteKeep, UiText.Dialog_Cancel) { Items = users },
                UiText.Modpacks_DeleteWithModpacks);
        }

        return await dialogs.ConfirmAsync(new ConfirmPrompt(title, plainMessage, UiText.ModCollections_Delete, UiText.Dialog_Cancel))
            ? ConfirmChoice.Confirm
            : ConfirmChoice.Cancel;
    }

    /// <summary>Each user's name, with its install when that isn't the one in use.</summary>
    public static IReadOnlyList<string> UserNames(ModpackStore modpacks, IEnumerable<ModpackUser> users, string? installRoot) =>
    [
        .. users.Select(user => installRoot is not null && modpacks.IsSameInstall(user.InstallRoot, installRoot)
            ? user.Name
            : string.Format(CultureInfo.CurrentCulture, UiText.Modpacks_UserInInstall, user.Name, user.InstallRoot)),
    ];
}
