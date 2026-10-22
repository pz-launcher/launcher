using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using PzLauncher.Desktop.Lang;
using PzLauncher.Desktop.Services.Mods;
using PzLauncher.Desktop.Services.Shell;

namespace PzLauncher.Desktop.ViewModels.Mods.Modpacks;

/// <summary>The modpack name rule as dialog text, and the question before deleting a source modpacks use.</summary>
public static class ModpackPrompts
{
    /// <param name="except">The modpack being renamed or edited, which its own name doesn't clash with.</param>
    public static string? ValidateName(ModpackStore modpacks, string installRoot, string text, string? except = null) =>
        !ModpackName.IsValid(text) ? UiText.Modpacks_NameInvalid
        : modpacks.IsNameTaken(installRoot, text, except) ? UiText.Modpacks_NameTaken
        : null;

    /// <summary>
    /// Before deleting a mod preset or local collection that modpacks use: <see cref="ConfirmChoice.Confirm"/> deletes it and leaves the modpacks
    /// with their copy, <see cref="ConfirmChoice.Alternative"/> deletes them too.
    /// </summary>
    /// <param name="users">The modpacks' names, as shown.</param>
    public static Task<ConfirmChoice> ConfirmDeleteUsedAsync(IDialogService dialogs, string title, string message, IReadOnlyList<string> users) =>
        dialogs.ChooseAsync(
            new ConfirmPrompt(title, message, UiText.Modpacks_DeleteKeep, UiText.Dialog_Cancel) { Items = users },
            UiText.Modpacks_DeleteWithModpacks);

    /// <summary>Each user's name, with its install when that isn't the one in use.</summary>
    public static IReadOnlyList<string> UserNames(ModpackStore modpacks, IEnumerable<ModpackUser> users, string? installRoot) =>
    [
        .. users.Select(user => installRoot is not null && modpacks.IsSameInstall(user.InstallRoot, installRoot)
            ? user.Name
            : string.Format(CultureInfo.CurrentCulture, UiText.Modpacks_UserInInstall, user.Name, user.InstallRoot)),
    ];
}
