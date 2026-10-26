using System;
using System.Globalization;
using System.Threading.Tasks;
using PzLauncher.Desktop.Lang;
using PzLauncher.Desktop.Services.Shell.Dialogs;

namespace PzLauncher.Desktop.ViewModels.Mods.Collections;

/// <param name="Overwrites">The one already saved under <paramref name="Name"/> is replaced.</param>
public sealed record ResolvedName(string Name, bool Overwrites);

/// <summary>The question when a name to save under is taken: overwrite, another name, or cancel.</summary>
public static class NameClashPrompts
{
    /// <summary>The name when free, else the user's choice; <c>null</c> when cancelled.</summary>
    /// <param name="message">A format taking the name.</param>
    /// <param name="askNewName">Asks for another name, starting from the taken one; <c>null</c> when cancelled.</param>
    public static async Task<ResolvedName?> ResolveAsync(
        IDialogService dialogs, Func<string, bool> exists, string title, string message, string name, Func<string, Task<string?>> askNewName)
    {
        if (!exists(name))
        {
            return new ResolvedName(name, Overwrites: false);
        }

        var prompt = new ConfirmPrompt(
            title, string.Format(CultureInfo.CurrentCulture, message, name), UiText.ModCollections_Overwrite, UiText.Dialog_Cancel);
        return await dialogs.ChooseAsync(prompt, UiText.Modpacks_NewName) switch
        {
            ConfirmChoice.Confirm => new ResolvedName(name, Overwrites: true),
            ConfirmChoice.Alternative => await askNewName(name) is { } other ? new ResolvedName(other, Overwrites: false) : null,
            ConfirmChoice.Cancel => null,
        };
    }
}
