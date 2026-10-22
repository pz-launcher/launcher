using System;
using System.Collections.Generic;
using System.Linq;
using PzLauncher.Core.Mod.Collections;
using PzLauncher.Core.Platform;
using PzLauncher.Desktop.Services.Preferences;

namespace PzLauncher.Desktop.Services.Mods;

/// <summary>
/// Each install's modpacks, in the user's order, looked up with the platform's path comparison and persisted through <see cref="IPreferenceStore"/>.
/// Names are unique per install (ordinal) and valid by <see cref="ModpackName"/>. A linked copy follows its source whenever the source is read;
/// a source gone after a successful read makes the modpack independent of it. Every change applies at once; the file write is fire-and-forget.
/// </summary>
public sealed class ModpackStore
{
    private readonly IPreferenceStore _store;
    private readonly LocalCollections _localCollections;
    private readonly StringComparison _pathComparison;
    private readonly InstallKeyedMap<IReadOnlyList<Modpack>> _modpacks;

    public ModpackStore(IPreferenceStore store, UserPreferences saved, IPlatformEnvironment platform, LocalCollections localCollections)
    {
        _store = store;
        _localCollections = localCollections;
        _pathComparison = platform.PathComparison;
        _modpacks = new InstallKeyedMap<IReadOnlyList<Modpack>>(saved.Modpacks, platform.PathComparison);

        // Never unsubscribed: both are app-lifetime singletons.
        localCollections.Changed += (_, _) => SyncLocalCollections();
        SyncLocalCollections();
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
    public void Save(string installRoot, Modpack modpack, string? original = null)
    {
        var saved = modpack with { Name = CheckedName(installRoot, modpack.Name, original) };
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

    public void SetExtraItems(string installRoot, string name, IReadOnlyList<ulong> items) =>
        Update(installRoot, modpack => modpack.Name == name ? modpack with { ExtraItems = items } : modpack);

    /// <summary>The modpack's preset copy follows the install's preset of this name from now on.</summary>
    public void LinkPreset(string installRoot, string name, string presetName) =>
        Update(installRoot, modpack => modpack.Name == name ? modpack with { Preset = modpack.Preset with { Name = presetName, IsLinked = true } } : modpack);

    /// <summary>
    /// After a successful read of the install's presets: a linked copy takes its preset's mods, and a link to a preset no longer there is dropped.
    /// A failed read must not call this.
    /// </summary>
    public void SyncPresets(string installRoot, IReadOnlyList<ModCollection> presets) =>
        Update(installRoot, modpack =>
        {
            if (!modpack.Preset.IsLinked)
            {
                return modpack;
            }

            var preset = presets.FirstOrDefault(candidate => candidate.Name == modpack.Preset.Name);
            return modpack with { Preset = preset is null ? modpack.Preset with { IsLinked = false } : modpack.Preset with { ModIds = preset.ModIds } };
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

    // Local collections are kept in memory, so they are always read successfully.
    private void SyncLocalCollections()
    {
        foreach (var root in _modpacks.Roots.ToList())
        {
            Update(root, modpack =>
            {
                if (modpack.Collection is not ModpackCollection.Local { Linked: { } id } local)
                {
                    return modpack;
                }

                return modpack with
                {
                    Collection = _localCollections.Find(id) is { } source
                        ? local with { Name = source.Name, Items = source.Items }
                        : local with { Linked = null },
                };
            });
        }
    }

    // A name the caller didn't validate is a bug, not an expected failure.
    private string CheckedName(string installRoot, string name, string? except)
    {
        if (!ModpackName.IsValid(name) || IsNameTaken(installRoot, name, except))
        {
            throw new ArgumentException($"'{name}' is not a free modpack name.", nameof(name));
        }

        return name.Trim();
    }

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
        var key = modpacks.Count == 0 ? _modpacks.Remove(installRoot) ?? installRoot : _modpacks.Set(installRoot, modpacks);

        // SaveModpacksAsync never throws for I/O, and a failed save must not undo the change.
        _ = _store.SaveModpacksAsync(key, modpacks);
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
