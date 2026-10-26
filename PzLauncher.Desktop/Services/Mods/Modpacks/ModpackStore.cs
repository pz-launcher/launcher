using System;
using System.Collections.Generic;
using System.Linq;
using PzLauncher.Core.Mod;
using PzLauncher.Core.Mod.Collections;
using PzLauncher.Core.Mod.Modpacks;
using PzLauncher.Core.Platform;
using PzLauncher.Desktop.Services.Mods.LocalCollections;
using PzLauncher.Desktop.Services.Mods.Library;
using PzLauncher.Desktop.Services.Preferences.Records;

namespace PzLauncher.Desktop.Services.Mods.Modpacks;

/// <summary>
/// Each install's modpacks, in the user's order, looked up with the platform's path comparison and persisted through <see cref="IModLibraryFile"/>.
/// Names are unique per install (ordinal) and valid by <see cref="ModpackNames"/>. A linked copy follows its source whenever the source is read;
/// a source gone after a successful read makes the modpack independent of it. Every change applies at once and saves every install's modpacks
/// once; the file write is fire-and-forget.
/// </summary>
public sealed class ModpackStore
{
    private readonly IModLibraryFile _file;
    private readonly LocalCollectionStore _localCollections;
    private readonly StringComparison _pathComparison;
    private readonly InstallKeyedMap<IReadOnlyList<Modpack>> _modpacks;

    // A local collection once here and missing now was deleted in the launcher.
    private readonly HashSet<Guid> _seenLocalCollections = [];

    public ModpackStore(IModLibraryFile file, ModLibrary saved, IPlatformEnvironment platform, LocalCollectionStore localCollections)
    {
        _file = file;
        _localCollections = localCollections;
        _pathComparison = platform.PathComparison;
        _modpacks = new InstallKeyedMap<IReadOnlyList<Modpack>>(
            saved.Modpacks.ToDictionary(entry => entry.Key, IReadOnlyList<Modpack> (entry) => [.. entry.Value.Select(Modpack.FromSaved)]),
            platform.PathComparison);
        _seenLocalCollections.UnionWith(localCollections.All.Select(collection => collection.Id));

        // Never unsubscribed: both are app-lifetime singletons.
        localCollections.Changed += (_, _) =>
        {
            _seenLocalCollections.UnionWith(localCollections.All.Select(collection => collection.Id));
            SyncLocalCollections(_seenLocalCollections.Contains);
        };

        // After a load that lost local collections, a missing one may be a lost one: its links stay.
        SyncLocalCollections(_ => saved.LocalCollectionsComplete);
    }

    /// <summary>Raised after each change that applied, for any install; never when nothing changed.</summary>
    public event EventHandler? Changed;

    /// <summary>In the user's order: new ones go to the end.</summary>
    public IReadOnlyList<Modpack> For(string installRoot) => _modpacks.GetValueOrDefault(installRoot) ?? [];

    public Modpack? Find(string installRoot, string name) => For(installRoot).FirstOrDefault(modpack => modpack.Name == name);

    /// <param name="except">The modpack being renamed or edited, which its own name doesn't clash with.</param>
    public bool IsNameTaken(string installRoot, string name, string? except = null)
    {
        var trimmed = name.Trim();
        return trimmed != except && For(installRoot).Any(modpack => modpack.Name == trimmed);
    }

    public bool IsSameInstall(string installRoot, string other) => string.Equals(installRoot, other, _pathComparison);

    /// <summary>Adds the modpack at the end, or replaces <paramref name="original"/> in its place.</summary>
    public void Save(string installRoot, Modpack modpack, string? original = null) =>
        _ = TrySave(installRoot, modpack with { Name = CheckedName(installRoot, modpack.Name, original) }, original);

    /// <summary><see cref="Save"/>; false, changing nothing, when the name is invalid or taken by another than <paramref name="original"/>.</summary>
    public bool TrySave(string installRoot, Modpack modpack, string? original = null)
    {
        if (!IsFreeName(installRoot, modpack.Name, original))
        {
            return false;
        }

        var saved = modpack with { Name = modpack.Name.Trim() };
        var current = For(installRoot);
        List<Modpack> modpacks = [.. current];
        var index = original is null ? -1 : modpacks.FindIndex(existing => existing.Name == original);
        if (index >= 0)
        {
            modpacks[index] = saved;
        }
        else
        {
            modpacks.Add(saved);
        }

        if (!modpacks.SequenceEqual(current))
        {
            Commit(installRoot, modpacks);
        }

        return true;
    }

    public void Rename(string installRoot, string original, string name)
    {
        var trimmed = CheckedName(installRoot, name, original);
        Update(installRoot, modpack => modpack.Name == original ? modpack with { Name = trimmed } : modpack);
    }

    /// <summary>Callers ask the user first.</summary>
    public void Delete(string installRoot, IReadOnlyCollection<string> names)
    {
        var current = For(installRoot);
        List<Modpack> kept = [.. current.Where(modpack => !names.Contains(modpack.Name))];
        if (kept.Count != current.Count)
        {
            Commit(installRoot, kept);
        }
    }

    /// <summary>Callers ask the user first.</summary>
    public void DeleteUsersOfLocalCollection(Guid id) =>
        UpdateAll(modpacks => [.. modpacks.Where(modpack => modpack.Collection is not ModpackCollection.Local { Linked: { } linked } || linked != id)]);

    /// <summary>Recomputes the modpack's extra items by <see cref="ModpackExtraItems.Compute"/>; true when they changed and were saved.</summary>
    public bool RefreshExtraItems(string installRoot, string name, IReadOnlyList<InstalledMod> installed, IReadOnlyCollection<ulong> collectionItems)
    {
        if (Find(installRoot, name) is not { } modpack)
        {
            return false;
        }

        var refreshed = WithExtraItems(modpack, installed, collectionItems);
        if (refreshed == modpack)
        {
            return false;
        }

        Update(installRoot, other => other.Name == name ? refreshed : other);
        return true;
    }

    /// <summary>The modpack's preset copy follows the install's preset of this name from now on.</summary>
    public void LinkPreset(string installRoot, string name, string presetName) =>
        Update(installRoot, modpack => modpack.Name == name ? modpack with { Preset = modpack.Preset with { Name = presetName, IsLinked = true } } : modpack);

    /// <summary>
    /// After a successful read of the install's presets and mods: a linked copy takes its preset's mods, a link to a preset no longer there is
    /// dropped, and the extra items are recomputed where the collection's items are known (a Steam one's from <paramref name="steamCollectionItems"/>),
    /// all in one save. A failed read must not call this.
    /// </summary>
    public void SyncPresets(
        string installRoot,
        IReadOnlyList<ModCollection> presets,
        IReadOnlyList<InstalledMod> installed,
        Func<ulong, IReadOnlyCollection<ulong>?> steamCollectionItems) =>
        Update(installRoot, modpack =>
        {
            if (modpack.Preset.IsLinked)
            {
                var preset = presets.FirstOrDefault(candidate => candidate.Name == modpack.Preset.Name);
                modpack = modpack with
                {
                    Preset = preset is null ? modpack.Preset with { IsLinked = false } : modpack.Preset with { ModIds = preset.ModIds },
                };
            }

            IReadOnlyCollection<ulong>? items = modpack.Collection switch
            {
                ModpackCollection.Steam steam => steamCollectionItems(steam.Id),
                ModpackCollection.Local local => [.. local.Items.Select(item => item.Id)],
                _ => throw new InvalidOperationException($"Unknown modpack collection {modpack.Collection}."),
            };
            return items is null ? modpack : WithExtraItems(modpack, installed, items);
        });

    /// <summary>The launcher renamed the install's preset: links follow it.</summary>
    public void RenamePreset(string installRoot, string original, string name) =>
        Update(installRoot, modpack => IsLinkedTo(modpack, original) ? modpack with { Preset = modpack.Preset with { Name = name } } : modpack);

    /// <summary>The launcher deleted the install's preset: the modpacks using it keep their copy.</summary>
    public void UnlinkPreset(string installRoot, string name) =>
        Update(installRoot, modpack => IsLinkedTo(modpack, name) ? modpack with { Preset = modpack.Preset with { IsLinked = false } } : modpack);

    /// <summary>The install's modpacks linked to its preset of this name, in order.</summary>
    public IReadOnlyList<string> UsersOfPreset(string installRoot, string name) =>
        [.. For(installRoot).Where(modpack => IsLinkedTo(modpack, name)).Select(modpack => modpack.Name)];

    /// <summary>Every install's modpacks linked to the local collection.</summary>
    public IReadOnlyList<ModpackUser> UsersOfLocalCollection(Guid id) =>
    [
        .. _modpacks.Roots.SelectMany(root => For(root)
            .Where(modpack => modpack.Collection is ModpackCollection.Local { Linked: { } linked } && linked == id)
            .Select(modpack => new ModpackUser(root, modpack.Name))),
    ];

    private static bool IsLinkedTo(Modpack modpack, string presetName) => modpack.Preset.IsLinked && modpack.Preset.Name == presetName;

    private static Modpack WithExtraItems(Modpack modpack, IReadOnlyList<InstalledMod> installed, IReadOnlyCollection<ulong> collectionItems) =>
        modpack with { ExtraItems = ModpackExtraItems.Compute(modpack.ExtraItems, modpack.Preset.ModIds, installed, collectionItems) };

    /// <param name="wasDeleted">Whether a linked local collection that is missing was deleted, which makes its users independent.</param>
    private void SyncLocalCollections(Func<Guid, bool> wasDeleted) =>
        UpdateAll(modpacks => [.. modpacks.Select(modpack => modpack.Collection is ModpackCollection.Local { Linked: { } id } local
            ? modpack with
            {
                Collection = _localCollections.Find(id) is { } source
                    ? local with { Name = source.Name, Items = source.Items }
                    : wasDeleted(id) ? local with { Linked = null } : local,
            }
            : modpack)]);

    /// <summary>Changes every install's modpacks, saving once.</summary>
    private void UpdateAll(Func<IReadOnlyList<Modpack>, List<Modpack>> change)
    {
        var changed = false;
        foreach (var root in _modpacks.Roots.ToList())
        {
            var current = _modpacks[root];
            var updated = change(current);
            if (updated.SequenceEqual(current))
            {
                continue;
            }

            if (updated.Count == 0)
            {
                _modpacks.Remove(root);
            }
            else
            {
                _modpacks.Set(root, updated);
            }

            changed = true;
        }

        if (changed)
        {
            Commit();
        }
    }

    private bool IsFreeName(string installRoot, string name, string? except) => ModpackNames.IsValid(name) && !IsNameTaken(installRoot, name, except);

    // A name the caller didn't validate is a bug, not an expected failure.
    private string CheckedName(string installRoot, string name, string? except) =>
        IsFreeName(installRoot, name, except) ? name.Trim() : throw new ArgumentException($"'{name}' is not a free modpack name.", nameof(name));

    private void Update(string installRoot, Func<Modpack, Modpack> change)
    {
        var current = For(installRoot);
        List<Modpack> changed = [.. current.Select(change)];
        if (!changed.SequenceEqual(current))
        {
            Commit(installRoot, changed);
        }
    }

    private void Commit(string installRoot, IReadOnlyList<Modpack> modpacks)
    {
        if (modpacks.Count == 0)
        {
            _modpacks.Remove(installRoot);
        }
        else
        {
            _modpacks.Set(installRoot, modpacks);
        }

        Commit();
    }

    private void Commit()
    {
        var saved = _modpacks.Roots.ToDictionary(
            root => root, IReadOnlyList<SavedModpack> (root) => [.. _modpacks[root].Select(modpack => modpack.ToSaved())], StringComparer.Ordinal);

        // SaveModpacksAsync never throws for I/O, and a failed save must not undo the change.
        _ = _file.SaveModpacksAsync(saved);
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
