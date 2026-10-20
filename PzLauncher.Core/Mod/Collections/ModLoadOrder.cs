namespace PzLauncher.Core.Mod.Collections;

/// <summary>The game's mod ordering rules (build 42.21), over the installed mods' <c>mod.info</c> lists.</summary>
public static class ModLoadOrder
{
    /// <summary>
    /// The enabled list choosing this collection in-game gives, without the game's hash order: collection order, each
    /// mod's installed requirements (recursively) before it, a mod incompatible with an enabled one left out.
    /// </summary>
    public static ApplyModCollectionResult Resolve(IReadOnlyList<string> modIds, IReadOnlyList<InstalledMod> installedMods)
    {
        var mods = ById(installedMods);
        var incompatible = IncompatibilityMap(mods.Values);
        var enabled = new List<string>();
        var activated = new Dictionary<string, int>(StringComparer.Ordinal);
        var missing = new List<string>();
        var skipped = new List<ModConflict>();
        var reported = new HashSet<string>(StringComparer.Ordinal);

        // Like the game, a mod is enabled before its requirements, so a requirement incompatible with it is the one left out.
        bool TryActivate(InstalledMod mod)
        {
            if (activated.ContainsKey(mod.Id))
            {
                return false;
            }

            if (incompatible.TryGetValue(mod.Id, out var others)
                && others.Where(activated.ContainsKey).MinBy(other => activated[other]) is { } conflict)
            {
                if (reported.Add(mod.Id))
                {
                    skipped.Add(new ModConflict(mod.Id, conflict));
                }

                return false;
            }

            activated.Add(mod.Id, activated.Count);
            return true;
        }

        // Depth-first with an explicit stack: require chains come from mod.info files, so their depth is untrusted.
        var stack = new Stack<(InstalledMod Mod, int Next)>();
        foreach (var id in modIds)
        {
            if (!mods.TryGetValue(id, out var root))
            {
                if (reported.Add(id))
                {
                    missing.Add(id);
                }

                continue;
            }

            if (!TryActivate(root))
            {
                continue;
            }

            stack.Push((root, 0));
            while (stack.TryPop(out var frame))
            {
                var descended = false;
                for (var i = frame.Next; i < frame.Mod.Require.Count; i++)
                {
                    if (mods.TryGetValue(frame.Mod.Require[i], out var requirement) && TryActivate(requirement))
                    {
                        stack.Push((frame.Mod, i + 1));
                        stack.Push((requirement, 0));
                        descended = true;
                        break;
                    }
                }

                if (!descended)
                {
                    enabled.Add(frame.Mod.Id);
                }
            }
        }

        return new ApplyModCollectionResult(enabled, missing, skipped);
    }

    /// <summary>
    /// The game's auto sort of a load order: by id, then each mod's <c>loadAfter</c> and <c>require</c> ids before it and
    /// its <c>loadBefore</c> ids after it. Ids not installed keep their order and go last.
    /// </summary>
    public static IReadOnlyList<string> AutoSort(IReadOnlyList<string> modIds, IReadOnlyList<InstalledMod> installedMods)
    {
        var mods = ById(installedMods);
        var members = modIds.Where(mods.ContainsKey).Distinct(StringComparer.Ordinal).ToList();
        var memberSet = new HashSet<string>(members, StringComparer.Ordinal);

        var order = new List<string>();
        var added = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in members.Order(StringComparer.Ordinal))
        {
            var mod = mods[id];
            var before = new List<string>();
            foreach (var other in mod.LoadAfter.Concat(mod.Require))
            {
                if (added.Add(other))
                {
                    before.Add(other);
                }
            }

            var after = new List<string>();
            foreach (var other in mod.LoadBefore)
            {
                if (added.Add(other))
                {
                    after.Add(other);
                }
            }

            var index = added.Contains(id) ? order.IndexOf(id) : -1;
            if (index >= 0)
            {
                // The game inserts each one at the same spot, so both groups end up reversed around the mod.
                foreach (var other in after)
                {
                    order.Insert(index + 1, other);
                }

                foreach (var other in before)
                {
                    order.Insert(index, other);
                }
            }
            else
            {
                order.AddRange(before);
                order.Add(id);
                order.AddRange(after);
                added.Add(id);
            }
        }

        var sorted = order.Where(memberSet.Contains).Distinct(StringComparer.Ordinal).ToList();
        sorted.AddRange(modIds.Where(id => !memberSet.Contains(id)).Distinct(StringComparer.Ordinal));
        return sorted;
    }

    /// <summary>The ids of <paramref name="mods"/> the game won't enable together with <paramref name="mod"/>, in their order, each once.</summary>
    public static IReadOnlyList<string> Conflicts(InstalledMod mod, IReadOnlyList<InstalledMod> mods)
    {
        if (!IncompatibilityMap(mods.Append(mod)).TryGetValue(mod.Id, out var incompatible))
        {
            return [];
        }

        return [.. mods.Select(other => other.Id).Where(id => id != mod.Id && incompatible.Contains(id)).Distinct(StringComparer.Ordinal)];
    }

    private static Dictionary<string, InstalledMod> ById(IReadOnlyList<InstalledMod> installedMods)
    {
        var mods = new Dictionary<string, InstalledMod>(StringComparer.Ordinal);
        foreach (var mod in installedMods)
        {
            mods.TryAdd(mod.Id, mod);
        }

        return mods;
    }

    // The game records incompatibility both ways.
    private static Dictionary<string, HashSet<string>> IncompatibilityMap(IEnumerable<InstalledMod> mods)
    {
        var map = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        void Link(string id, string other)
        {
            if (!map.TryGetValue(id, out var set))
            {
                set = new HashSet<string>(StringComparer.Ordinal);
                map.Add(id, set);
            }

            set.Add(other);
        }

        foreach (var mod in mods)
        {
            foreach (var other in mod.Incompatible)
            {
                Link(mod.Id, other);
                Link(other, mod.Id);
            }
        }

        return map;
    }
}
