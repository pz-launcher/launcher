using System.Globalization;
using System.Text;

namespace PzLauncher.Core.Steam.Workshop;

/// <summary>Starred Workshop collections and items as share text: <see cref="CollectionsMarker"/> and one collection id per line, then <see cref="ItemsMarker"/> and the item ids.</summary>
public static class WorkshopFavoritesShareText
{
    // Fixed ASCII, never localized: text shared from any UI language imports in any other.
    public const string CollectionsMarker = "#collections";

    public const string ItemsMarker = "#items";

    public static string Format(IEnumerable<ulong> collections, IEnumerable<ulong> items)
    {
        var text = new StringBuilder();
        AppendSection(text, CollectionsMarker, collections);
        AppendSection(text, ItemsMarker, items);
        return text.ToString();
    }

    /// <summary>Ids and Workshop links under each marker, each id once per list; markers match ignoring case, blank lines are ignored.</summary>
    public static SharedWorkshopFavorites Parse(string text)
    {
        List<ulong> collections = [];
        List<ulong> items = [];
        List<ulong> unmarked = [];
        var skipped = 0;
        List<ulong>? section = null;
        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            if (trimmed.Equals(CollectionsMarker, StringComparison.OrdinalIgnoreCase))
            {
                section = collections;
            }
            else if (trimmed.Equals(ItemsMarker, StringComparison.OrdinalIgnoreCase))
            {
                section = items;
            }
            else if (WorkshopLinks.TryParseItemId(trimmed, out var id))
            {
                var list = section ?? unmarked;
                if (!list.Contains(id))
                {
                    list.Add(id);
                }
            }
            else
            {
                skipped++;
            }
        }

        unmarked.RemoveAll(id => collections.Contains(id) || items.Contains(id));
        return new SharedWorkshopFavorites(collections, items, unmarked, skipped);
    }

    private static void AppendSection(StringBuilder text, string marker, IEnumerable<ulong> ids)
    {
        text.Append(marker).Append('\n');
        foreach (var id in ids)
        {
            text.Append(id.ToString(CultureInfo.InvariantCulture)).Append('\n');
        }
    }
}

/// <param name="Unmarked">Ids before any marker that neither section lists; whether each is a collection is not known yet.</param>
/// <param name="SkippedLines">Lines that are none of blank, a marker, an id or a Workshop link.</param>
public sealed record SharedWorkshopFavorites(IReadOnlyList<ulong> Collections, IReadOnlyList<ulong> Items, IReadOnlyList<ulong> Unmarked, int SkippedLines)
{
    public bool HasIds => Collections.Count > 0 || Items.Count > 0 || Unmarked.Count > 0;
}
