using System;
using System.Threading.Tasks;
using PzLauncher.Desktop.Lang;
using PzLauncher.Desktop.Services.Mods.LocalCollections;
using PzLauncher.Desktop.Services.Shell;
using PzLauncher.Desktop.Services.Shell.Dialogs;
using PzLauncher.Desktop.ViewModels.Mods.Modpacks;

namespace PzLauncher.Desktop.ViewModels.Mods.Workshop.LocalCollections;

/// <summary>The local collection name rule as dialog text, and the name dialog.</summary>
public static class LocalCollectionPrompts
{
    /// <param name="except">The collection being renamed, which its own name doesn't clash with.</param>
    public static string? ValidateName(LocalCollectionStore collections, string text, Guid? except = null) =>
        ModpackPrompts.NameError(
            text, name => collections.IsNameTaken(name, except), UiText.LocalCollections_NameInvalid, UiText.LocalCollections_NameTaken);

    /// <summary>The trimmed name; <c>null</c> when cancelled.</summary>
    /// <param name="except"><inheritdoc cref="ValidateName" path="/param[@name='except']"/></param>
    public static Task<string?> AskNameAsync(
        IDialogService dialogs, LocalCollectionStore collections, string title, string initial, string accept, Guid? except = null) =>
        ModpackPrompts.AskTrimmedAsync(
            dialogs, new TextPrompt(title, UiText.LocalCollections_NameMessage, initial, accept, text => ValidateName(collections, text, except)));
}
