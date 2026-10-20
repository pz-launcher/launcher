using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace PzLauncher.Core.Steam.Workshop;

/// <summary>Number formats Steam's Workshop files and Web API share.</summary>
internal static class WorkshopIds
{
    public static bool TryParse(string? text, out ulong id) =>
        ulong.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out id) && id != 0;

    // Manifest ids are unsigned 64-bit; the canonical text makes the acf's and the API's compare equal.
    public static bool TryParseManifest(string? text, [NotNullWhen(true)] out string? manifest)
    {
        manifest = ulong.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            ? value.ToString(CultureInfo.InvariantCulture)
            : null;
        return manifest is not null;
    }

    public static bool TryParseUnixTime(string? text, out DateTimeOffset time) =>
        TryParseUnixTime(long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds) ? seconds : -1, out time);

    public static bool TryParseUnixTime(long seconds, out DateTimeOffset time)
    {
        var valid = seconds >= 0 && seconds <= DateTimeOffset.MaxValue.ToUnixTimeSeconds();
        time = valid ? DateTimeOffset.FromUnixTimeSeconds(seconds) : default;
        return valid;
    }

    public static bool TryParseSize(string? text, out long size) =>
        long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out size);
}
