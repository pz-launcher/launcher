using System;
using System.Globalization;
using System.Threading.Tasks;
using PzLauncher.Core.Mod.Collections;
using PzLauncher.Desktop.Lang;
using PzLauncher.Desktop.Services.Shell.Dialogs;
using PzLauncher.Desktop.Services.Shell;

namespace PzLauncher.Desktop.ViewModels.Mods.Collections;

/// <summary>The collection name dialog: the game's name rule, then a confirm before overwriting another collection.</summary>
public static class ModCollectionPrompts
{
    public static string? ValidateName(string text) =>
        ModCollectionName.IsValid(text.Trim()) ? null : UiText.ModCollections_NameInvalid;

    public static TextPrompt NamePrompt(string title, string initial, string accept) =>
        new(title, UiText.ModCollections_NameMessage, initial, accept, ValidateName);

    /// <summary><see cref="TextPromptResult.Accepted"/> carries the trimmed name; a declined overwrite asks for the name again.</summary>
    /// <param name="clashes">Whether saving under the name would replace another collection.</param>
    public static async Task<TextPromptResult> AskNameAsync(IDialogService dialogs, TextPrompt prompt, Func<string, bool> clashes)
    {
        while (true)
        {
            var result = await dialogs.PromptTextAsync(prompt);
            if (result is not TextPromptResult.Accepted accepted)
            {
                return result;
            }

            var name = accepted.Text.Trim();
            if (!clashes(name) || await ConfirmOverwriteAsync(dialogs, name))
            {
                return new TextPromptResult.Accepted(name);
            }

            prompt = prompt with { InitialText = name };
        }
    }

    public static Task<bool> ConfirmOverwriteAsync(IDialogService dialogs, string name) =>
        dialogs.ConfirmAsync(new ConfirmPrompt(
            UiText.ModCollections_OverwriteTitle,
            string.Format(CultureInfo.CurrentCulture, UiText.ModCollections_OverwriteMessage, name),
            UiText.ModCollections_Overwrite,
            UiText.Dialog_Cancel));
}
