using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PzLauncher.Core.Common;
using PzLauncher.Core.Mod.Modpacks;
using PzLauncher.Desktop.Services.Mods.LocalCollections;

namespace PzLauncher.Desktop.Services.Mods.Modpacks;

/// <summary>The names a pasted modpack is kept under, each clash settled by the user.</summary>
/// <param name="ReplacesModpack">The install's modpack of this name is overwritten.</param>
/// <param name="Local">For a local collection; <c>null</c> keeps its shared name.</param>
public sealed record ModpackImportNames(string Modpack, bool ReplacesModpack, string Preset, ModpackImportLocalName? Local);

/// <param name="Replaces">The local collection overwritten; <c>null</c> when a new one is made.</param>
public sealed record ModpackImportLocalName(string Name, Guid? Replaces);

public enum ModpackImportNameKind
{
    Modpack,
    LocalCollection,
}

public abstract record ModpackImportResult
{
    private ModpackImportResult()
    {
    }

    public sealed record Imported(string Name) : ModpackImportResult;

    /// <summary>Taken since the user chose it; what was written before stays.</summary>
    public sealed record NameTaken(ModpackImportNameKind Kind, string Name) : ModpackImportResult;

    /// <summary>The preset write failed; nothing was written.</summary>
    public sealed record Failed(Error Error) : ModpackImportResult;
}

/// <summary>Keeps a pasted modpack: its preset in the game's files, its local collection or opened Steam collection, then the modpack linked to both.</summary>
public sealed class ModpackImport(PresetWriter presets, LocalCollectionStore localCollections, ModpackStore modpacks, WorkshopCollectionHistory history)
{
    /// <summary>Runs inside the page's write of the install's files; the caller reads the presets again afterwards.</summary>
    /// <param name="steamTitle">The Steam collection's title as last seen, for the opened collections.</param>
    public async Task<ModpackImportResult> ImportAsync(
        string installRoot, string extraGameArgs, SharedModpack shared, ModpackImportNames names, string? steamTitle = null, CancellationToken ct = default)
    {
        var local = shared.Collection is SharedModpackCollection.Local sharedLocal ? names.Local ?? new ModpackImportLocalName(sharedLocal.Name.Trim(), null) : null;
        if (TakenName(installRoot, names, local) is { } taken)
        {
            return taken;
        }

        var saved = await presets.SaveAsync(installRoot, extraGameArgs, names.Preset, shared.Preset.ModIds, ct: ct);
        if (!saved.IsSuccess)
        {
            return new ModpackImportResult.Failed(saved.Error);
        }

        ModpackCollection collection;
        switch (shared.Collection)
        {
            case SharedModpackCollection.Steam steam:
                history.Remember(steam.Id, steamTitle);
                collection = new ModpackCollection.Steam(steam.Id);
                break;
            case SharedModpackCollection.Local { Items: var ids } when local is not null:
                List<WorkshopEntry> items = [.. ids.Select(id => new WorkshopEntry(id, null))];

                // A replaced collection deleted meanwhile is made again.
                var kept = (local.Replaces is { } replaced ? localCollections.Replace(replaced, items) : null) ?? localCollections.TryCreate(local.Name, items);
                if (kept is null)
                {
                    return new ModpackImportResult.NameTaken(ModpackImportNameKind.LocalCollection, local.Name);
                }

                collection = new ModpackCollection.Local(kept.Id, kept.Name, kept.Items);
                break;
            default:
                throw new ArgumentException($"Unknown shared collection {shared.Collection}.", nameof(shared));
        }

        var modpack = new Modpack(names.Modpack, new ModpackPreset(names.Preset, shared.Preset.ModIds, IsLinked: true), collection, shared.ExtraItems);
        return modpacks.TrySave(installRoot, modpack, names.ReplacesModpack ? names.Modpack : null)
            ? new ModpackImportResult.Imported(modpack.Name.Trim())
            : new ModpackImportResult.NameTaken(ModpackImportNameKind.Modpack, names.Modpack);
    }

    // Checked before writing anything; the stores check again, as the preset write lets other changes in.
    private ModpackImportResult.NameTaken? TakenName(string installRoot, ModpackImportNames names, ModpackImportLocalName? local) =>
        !names.ReplacesModpack && modpacks.IsNameTaken(installRoot, names.Modpack)
            ? new ModpackImportResult.NameTaken(ModpackImportNameKind.Modpack, names.Modpack)
            : local is { Replaces: null } && localCollections.IsNameTaken(local.Name)
                ? new ModpackImportResult.NameTaken(ModpackImportNameKind.LocalCollection, local.Name)
                : null;
}
