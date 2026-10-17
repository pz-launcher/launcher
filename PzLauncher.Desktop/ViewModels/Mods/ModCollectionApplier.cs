using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PzLauncher.Core.Mod;
using PzLauncher.Core.Mod.Collections;
using PzLauncher.Desktop.Lang;
using PzLauncher.Desktop.Services;

namespace PzLauncher.Desktop.ViewModels.Mods;

/// <summary>
/// Applying a collection as the enabled mod list, the same from the page and at launch: offer to keep an enabled list
/// no collection matches, announce what the apply leaves out, then write.
/// </summary>
public sealed class ModCollectionApplier(IModCollectionService collections, IDialogService dialogs)
{
    public async Task<ModCollectionApplyOutcome> ApplyAsync(string installRoot, string extraGameArgs, string name, CancellationToken ct = default)
    {
        var read = await collections.GetAsync(installRoot, extraGameArgs, ct);
        if (!read.IsSuccess)
        {
            return new ModCollectionApplyOutcome.Failed(read.Error);
        }

        var state = read.Value;
        if (!state.ActiveMatchesCollection)
        {
            var prompt = new TextPrompt(
                UiText.ModApply_KeepTitle,
                string.Format(CultureInfo.CurrentCulture, UiText.ModApply_KeepMessage, state.ActiveIds.Count, name),
                UiText.ModApply_KeepDefaultName,
                UiText.ModApply_KeepSave,
                ModCollectionPrompts.ValidateName,
                SkipText: UiText.ModApply_KeepSkip);
            switch (await ModCollectionPrompts.AskNameAsync(dialogs, prompt, keepName => Contains(state, keepName)))
            {
                case TextPromptResult.Accepted accepted:
                    var saved = await collections.SaveAsync(
                        new SaveModCollectionRequest(installRoot, extraGameArgs, null, accepted.Text, state.ActiveIds), ct);
                    if (!saved.IsSuccess)
                    {
                        return new ModCollectionApplyOutcome.Failed(saved.Error);
                    }

                    // The saved list may have replaced the collection being applied.
                    read = await collections.GetAsync(installRoot, extraGameArgs, ct);
                    if (!read.IsSuccess)
                    {
                        return new ModCollectionApplyOutcome.Failed(read.Error);
                    }

                    state = read.Value;
                    break;
                case TextPromptResult.Skipped:
                    break;
                case TextPromptResult.Cancelled:
                    return new ModCollectionApplyOutcome.Cancelled();
            }
        }

        // A collection missing here is left to ApplyAsync, which reports it as not found.
        if (state.Collections.FirstOrDefault(collection => collection.Name == name) is { } target)
        {
            var preview = ModLoadOrder.Resolve(target.ModIds, state.InstalledMods);
            if (preview.DropsAny && !await dialogs.ConfirmAsync(DropNotice(name, preview, state.InstalledMods)))
            {
                return new ModCollectionApplyOutcome.Cancelled();
            }
        }

        var applied = await collections.ApplyAsync(installRoot, extraGameArgs, name, ct);
        return applied.IsSuccess
            ? new ModCollectionApplyOutcome.Applied(applied.Value)
            : new ModCollectionApplyOutcome.Failed(applied.Error);
    }

    private static bool Contains(ModCollectionState state, string name) => state.Collections.Any(collection => collection.Name == name);

    private static ConfirmPrompt DropNotice(string name, ApplyModCollectionResult preview, IReadOnlyList<InstalledMod> installed)
    {
        var names = installed.ToDictionary(mod => mod.Id, mod => mod.Name, StringComparer.Ordinal);
        string Label(string id) => names.TryGetValue(id, out var modName) && modName != id
            ? string.Format(CultureInfo.CurrentCulture, UiText.Mods_NameWithId, modName, id)
            : id;

        return new ConfirmPrompt(
            UiText.ModApply_DropTitle,
            string.Format(CultureInfo.CurrentCulture, UiText.ModApply_DropMessage, name),
            UiText.ModApply_DropContinue,
            UiText.Dialog_Cancel)
        {
            Items =
            [
                .. preview.MissingIds.Select(id => string.Format(CultureInfo.CurrentCulture, UiText.ModApply_DropMissing, id)),
                .. preview.SkippedIncompatible.Select(conflict =>
                    string.Format(CultureInfo.CurrentCulture, UiText.ModApply_DropIncompatible, Label(conflict.Id), Label(conflict.ConflictsWith))),
            ],
        };
    }
}
