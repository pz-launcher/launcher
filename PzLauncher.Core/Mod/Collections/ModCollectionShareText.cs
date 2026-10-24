using System.Text;
using PzLauncher.Core.Mod.Collections.Files;

namespace PzLauncher.Core.Mod.Collections;

/// <summary>The game's collection share text: one <c>name:id1;id2;</c> line, as in its collection file.</summary>
public static class ModCollectionShareText
{
    public static string Format(string name, IReadOnlyList<string> modIds) => Line(name, modIds) + "\n";

    /// <summary>Every collection line in pasted text, in order; favourites lines and lines without a name are skipped.</summary>
    public static IReadOnlyList<SharedModCollection> Parse(string text)
    {
        var collections = new List<SharedModCollection>();
        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith(ModCollectionFiles.FavouritesPrefix, StringComparison.Ordinal) && ParseLine(trimmed) is { } collection)
            {
                collections.Add(collection);
            }
        }

        return collections;
    }

    internal static string Line(string name, IReadOnlyList<string> modIds)
    {
        var line = new StringBuilder(name).Append(ModCollectionFiles.NameSeparator);
        foreach (var id in modIds)
        {
            line.Append(id).Append(ModCollectionFiles.IdSeparator);
        }

        return line.ToString();
    }

    // As the game reads a line: split at the first ':', an empty name is no collection. It keeps ids as table keys,
    // so a repeated id counts once.
    internal static SharedModCollection? ParseLine(string line)
    {
        var separator = line.IndexOf(ModCollectionFiles.NameSeparator);
        if (separator <= 0)
        {
            return null;
        }

        var ids = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var part in line[(separator + 1)..].Split(ModCollectionFiles.IdSeparator))
        {
            var id = part.Trim();
            if (id.Length > 0 && seen.Add(id))
            {
                ids.Add(id);
            }
        }

        return new SharedModCollection(line[..separator], ids);
    }
}
