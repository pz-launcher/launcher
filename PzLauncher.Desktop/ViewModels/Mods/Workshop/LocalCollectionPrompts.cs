using System;
using System.Threading.Tasks;
using PzLauncher.Desktop.Lang;
using PzLauncher.Desktop.Services.Mods;
using PzLauncher.Desktop.Services.Shell;

namespace PzLauncher.Desktop.ViewModels.Mods.Workshop;

/// <summary>The local collection name rule as dialog text, and the name dialog.</summary>
public static class LocalCollectionPrompts
{
    /// <param name="except">The collection being renamed, which its own name doesn't clash with.</param>
    public static string? ValidateName(LocalCollections collections, string text, Guid? except = null) =>
        !LocalCollectionName.IsValid(text) ? UiText.LocalCollections_NameInvalid
        : collections.IsNameTaken(text, except) ? UiText.LocalCollections_NameTaken
        : null;

    /// <summary>The trimmed name; <c>null</c> when cancelled.</summary>
    /// <param name="except"><inheritdoc cref="ValidateName" path="/param[@name='except']"/></param>
    public static async Task<string?> AskNameAsync(
        IDialogService dialogs, LocalCollections collections, string title, string initial, string accept, Guid? except = null) =>
        await dialogs.PromptTextAsync(new TextPrompt(
            title, UiText.LocalCollections_NameMessage, initial, accept, text => ValidateName(collections, text, except))) is TextPromptResult.Accepted accepted
            ? accepted.Text.Trim()
            : null;
}
