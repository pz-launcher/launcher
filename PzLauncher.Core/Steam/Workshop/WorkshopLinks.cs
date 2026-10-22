using System.Globalization;

namespace PzLauncher.Core.Steam.Workshop;

/// <summary>Workshop item ids in text a user pastes, and the Steam client's page for an item.</summary>
public static class WorkshopLinks
{
    /// <summary>A bare item id, a steamcommunity.com link carrying <c>id=</c>, or a Steam client page link.</summary>
    public static bool TryParseItemId(string text, out ulong id)
    {
        var trimmed = text.Trim();
        if (WorkshopIds.TryParse(trimmed, out id))
        {
            return true;
        }

        if (trimmed.StartsWith(WorkshopFiles.ClientPageUrl, StringComparison.OrdinalIgnoreCase))
        {
            return WorkshopIds.TryParse(trimmed[WorkshopFiles.ClientPageUrl.Length..].TrimEnd('/'), out id);
        }

        // Links copied from an address bar can lack the scheme.
        var absolute = trimmed.Contains("://", StringComparison.Ordinal) ? trimmed : "https://" + trimmed;
        if (!Uri.TryCreate(absolute, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            return false;
        }

        var host = uri.Host;
        if (!host.Equals(WorkshopFiles.CommunityHost, StringComparison.OrdinalIgnoreCase)
            && !host.EndsWith("." + WorkshopFiles.CommunityHost, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        foreach (var pair in uri.Query.TrimStart('?').Split('&'))
        {
            var separator = pair.IndexOf('=', StringComparison.Ordinal);
            if (separator > 0
                && pair[..separator].Equals(WorkshopFiles.CommunityIdParameter, StringComparison.OrdinalIgnoreCase)
                && WorkshopIds.TryParse(pair[(separator + 1)..], out id))
            {
                return true;
            }
        }

        id = 0;
        return false;
    }

    /// <summary>One id or link per line, each id once in first-seen order; blank lines are ignored.</summary>
    public static PastedWorkshopIds ParseLines(string text)
    {
        List<ulong> ids = [];
        var skipped = 0;
        foreach (var line in text.Split('\n'))
        {
            if (line.Trim().Length == 0)
            {
                continue;
            }

            if (!TryParseItemId(line, out var id))
            {
                skipped++;
            }
            else if (!ids.Contains(id))
            {
                ids.Add(id);
            }
        }

        return new PastedWorkshopIds(ids, skipped);
    }

    public static Uri ClientPage(ulong id) => new(WorkshopFiles.ClientPageUrl + id.ToString(CultureInfo.InvariantCulture));
}

/// <param name="SkippedLines">Non-blank lines that are neither an id nor a Workshop link.</param>
public sealed record PastedWorkshopIds(IReadOnlyList<ulong> Ids, int SkippedLines);
