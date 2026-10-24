using System.Globalization;
using System.Text;
using PzLauncher.Core.Mod.Collections;
using PzLauncher.Core.Steam.Workshop.Ids;

namespace PzLauncher.Core.Mod.Modpacks;

/// <summary>
/// A modpack as share text: <see cref="Header"/> and the format version, then <see cref="NameMarker"/>, the collection, <see cref="ItemsMarker"/>
/// and, last so it can be copied into the game alone, <see cref="PresetMarker"/> with the game's preset line.
/// </summary>
public static class ModpackShareText
{
    public const int Version = 1;

    // Fixed ASCII, never localized: text shared from any UI language imports in any other.
    public const string Header = "#pzlauncher-modpack";

    /// <summary>The next line is the modpack's name.</summary>
    public const string NameMarker = "#name";

    /// <summary>The next line is the collection's id or link.</summary>
    public const string SteamCollectionMarker = "#steam-collection";

    /// <summary>The next line is the collection's name, then its item ids or links.</summary>
    public const string LocalCollectionMarker = "#local-collection";

    public const string ItemsMarker = "#items";

    /// <summary>The next line is the game's <c>name:id1;id2;</c> preset line.</summary>
    public const string PresetMarker = "#preset";

    public static string Format(SharedModpack modpack)
    {
        var text = new StringBuilder();
        AppendLine(text, Header + " " + Version.ToString(CultureInfo.InvariantCulture));
        AppendLine(text, NameMarker);
        AppendLine(text, modpack.Name);
        switch (modpack.Collection)
        {
            case SharedModpackCollection.Steam steam:
                AppendLine(text, SteamCollectionMarker);
                AppendIds(text, [steam.Id]);
                break;
            case SharedModpackCollection.Local local:
                AppendLine(text, LocalCollectionMarker);
                AppendLine(text, local.Name);
                AppendIds(text, local.Items);
                break;
        }

        if (modpack.ExtraItems.Count > 0)
        {
            AppendLine(text, ItemsMarker);
            AppendIds(text, modpack.ExtraItems);
        }

        AppendLine(text, PresetMarker);
        return text.Append(ModCollectionShareText.Format(modpack.Preset.Name, modpack.Preset.ModIds)).ToString();
    }

    /// <summary>
    /// Markers match ignoring case, blank lines are ignored and lines are trimmed. A name line is taken as it is, never as a marker or id.
    /// Lines under an unknown marker are ignored, so a later version can add sections.
    /// </summary>
    public static ModpackShareTextResult Parse(string text)
    {
        List<string> lines = [.. text.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0)];
        if (lines.Count == 0)
        {
            return new ModpackShareTextResult.NotModpack();
        }

        var header = lines[0].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (!header[0].Equals(Header, StringComparison.OrdinalIgnoreCase))
        {
            return new ModpackShareTextResult.NotModpack();
        }

        if (header.Length < 2 || !int.TryParse(header[1], NumberStyles.None, CultureInfo.InvariantCulture, out var version) || version < 1)
        {
            return new ModpackShareTextResult.Incomplete();
        }

        if (version > Version)
        {
            return new ModpackShareTextResult.NewerVersion(version);
        }

        string? name = null;
        ulong? steam = null;
        string? localName = null;
        List<ulong> localItems = [];
        List<ulong> extraItems = [];
        SharedModCollection? preset = null;
        var markers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var section = Section.None;
        var skipped = 0;
        foreach (var line in lines.Skip(1))
        {
            switch (section)
            {
                case Section.Name:
                    name = line;
                    section = Section.None;
                    continue;
                case Section.LocalCollectionName:
                    localName = line;
                    section = Section.LocalCollection;
                    continue;
                case Section.Preset:
                    preset = ModCollectionShareText.ParseLine(line);
                    section = Section.None;
                    continue;
            }

            if (line.StartsWith('#'))
            {
                section = MarkerSection(line);

                // A repeated section would leave it unclear which one is meant.
                if (section != Section.Unknown && !markers.Add(line))
                {
                    return new ModpackShareTextResult.Incomplete();
                }

                continue;
            }

            switch (section)
            {
                case Section.SteamCollection when WorkshopLinks.TryParseItemId(line, out var id) && steam is null:
                    steam = id;
                    break;
                case Section.LocalCollection when WorkshopLinks.TryParseItemId(line, out var id):
                    AddOnce(localItems, id);
                    break;
                case Section.Items when WorkshopLinks.TryParseItemId(line, out var id):
                    AddOnce(extraItems, id);
                    break;
                case Section.Unknown:
                    break;
                default:
                    skipped++;
                    break;
            }
        }

        var hasSteam = markers.Contains(SteamCollectionMarker);
        SharedModpackCollection? collection = (hasSteam, localName) switch
        {
            (true, null) when steam is { } id => new SharedModpackCollection.Steam(id),
            (false, { } listName) => new SharedModpackCollection.Local(listName, localItems),
            _ => null,
        };
        return name is not null && collection is not null && preset is not null
            ? new ModpackShareTextResult.Read(new SharedModpack(name, collection, extraItems, preset), skipped)
            : new ModpackShareTextResult.Incomplete();
    }

    private static Section MarkerSection(string line) =>
        line.Equals(NameMarker, StringComparison.OrdinalIgnoreCase) ? Section.Name
        : line.Equals(SteamCollectionMarker, StringComparison.OrdinalIgnoreCase) ? Section.SteamCollection
        : line.Equals(LocalCollectionMarker, StringComparison.OrdinalIgnoreCase) ? Section.LocalCollectionName
        : line.Equals(ItemsMarker, StringComparison.OrdinalIgnoreCase) ? Section.Items
        : line.Equals(PresetMarker, StringComparison.OrdinalIgnoreCase) ? Section.Preset
        : Section.Unknown;

    private static void AddOnce(List<ulong> ids, ulong id)
    {
        if (!ids.Contains(id))
        {
            ids.Add(id);
        }
    }

    private static void AppendLine(StringBuilder text, string line) => text.Append(line).Append('\n');

    private static void AppendIds(StringBuilder text, IEnumerable<ulong> ids)
    {
        foreach (var id in ids)
        {
            AppendLine(text, id.ToString(CultureInfo.InvariantCulture));
        }
    }

    private enum Section
    {
        None,
        Name,
        SteamCollection,
        LocalCollectionName,
        LocalCollection,
        Items,
        Preset,
        Unknown,
    }
}

/// <param name="ExtraItems">Workshop items of the preset's mods that the collection lacks.</param>
public sealed record SharedModpack(string Name, SharedModpackCollection Collection, IReadOnlyList<ulong> ExtraItems, SharedModCollection Preset);

public abstract record SharedModpackCollection
{
    private SharedModpackCollection()
    {
    }

    /// <summary>Shared by id: the receiver reads its current items.</summary>
    public sealed record Steam(ulong Id) : SharedModpackCollection;

    /// <summary>Shared as its items when shared.</summary>
    public sealed record Local(string Name, IReadOnlyList<ulong> Items) : SharedModpackCollection;
}

public abstract record ModpackShareTextResult
{
    private ModpackShareTextResult()
    {
    }

    /// <param name="SkippedLines">Lines that are none of blank, a marker, a name, the preset line, an id or a Workshop link where one belongs.</param>
    public sealed record Read(SharedModpack Modpack, int SkippedLines) : ModpackShareTextResult;

    /// <summary>The first line is no modpack header.</summary>
    public sealed record NotModpack : ModpackShareTextResult;

    /// <summary>Written by a later launcher in a format this one can't read.</summary>
    public sealed record NewerVersion(int Version) : ModpackShareTextResult;

    /// <summary>A modpack header, but the version, name, collection or preset is missing, unreadable or given twice.</summary>
    public sealed record Incomplete : ModpackShareTextResult;
}
