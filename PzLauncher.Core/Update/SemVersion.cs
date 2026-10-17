using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PzLauncher.Core.Update;

/// <summary>A SemVer 2.0 version without build metadata; compares by SemVer precedence.</summary>
/// <param name="Prerelease">Empty for a stable version.</param>
[JsonConverter(typeof(SemVersionJsonConverter))]
public sealed record SemVersion(int Major, int Minor, int Patch, string Prerelease) : IComparable<SemVersion>
{
    public static readonly SemVersion Development = new(0, 0, 0, "dev");

    public bool IsPrerelease => Prerelease.Length > 0;

    /// <summary>Local (<c>0.0.0-dev</c>) and CI (<c>0.0.0-ci.N</c>) builds.</summary>
    public bool IsDevelopment => Major == 0 && Minor == 0 && Patch == 0 && IsPrerelease;

    /// <summary>Build metadata after <c>+</c> is dropped; no <c>v</c> prefix.</summary>
    public static bool TryParse(string? text, [NotNullWhen(true)] out SemVersion? version)
    {
        version = null;
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        var plus = text.IndexOf('+', StringComparison.Ordinal);
        if (plus >= 0)
        {
            if (!AreValidIdentifiers(text[(plus + 1)..], allowLeadingZeros: true))
            {
                return false;
            }

            text = text[..plus];
        }

        var prerelease = "";
        var dash = text.IndexOf('-', StringComparison.Ordinal);
        if (dash >= 0)
        {
            prerelease = text[(dash + 1)..];
            if (!AreValidIdentifiers(prerelease, allowLeadingZeros: false))
            {
                return false;
            }

            text = text[..dash];
        }

        var parts = text.Split('.');
        if (parts.Length != 3
            || !TryParseNumber(parts[0], out var major)
            || !TryParseNumber(parts[1], out var minor)
            || !TryParseNumber(parts[2], out var patch))
        {
            return false;
        }

        version = new SemVersion(major, minor, patch, prerelease);
        return true;
    }

    /// <summary>An assembly's informational version; anything unparseable is <see cref="Development"/>.</summary>
    public static SemVersion FromInformationalVersion(string? informationalVersion) =>
        TryParse(informationalVersion, out var version) ? version : Development;

    public int CompareTo(SemVersion? other)
    {
        if (other is null)
        {
            return 1;
        }

        var core = (Major, Minor, Patch).CompareTo((other.Major, other.Minor, other.Patch));
        if (core != 0)
        {
            return core;
        }

        // A stable version outranks every prerelease of the same core version.
        return (IsPrerelease, other.IsPrerelease) switch
        {
            (false, false) => 0,
            (false, true) => 1,
            (true, false) => -1,
            (true, true) => ComparePrerelease(Prerelease, other.Prerelease),
        };
    }

    public static bool operator >(SemVersion left, SemVersion right) => left.CompareTo(right) > 0;

    public static bool operator <(SemVersion left, SemVersion right) => left.CompareTo(right) < 0;

    public static bool operator >=(SemVersion left, SemVersion right) => left.CompareTo(right) >= 0;

    public static bool operator <=(SemVersion left, SemVersion right) => left.CompareTo(right) <= 0;

    public override string ToString() =>
        IsPrerelease
            ? string.Create(CultureInfo.InvariantCulture, $"{Major}.{Minor}.{Patch}-{Prerelease}")
            : string.Create(CultureInfo.InvariantCulture, $"{Major}.{Minor}.{Patch}");

    private static int ComparePrerelease(string left, string right)
    {
        var a = left.Split('.');
        var b = right.Split('.');
        for (var i = 0; i < Math.Min(a.Length, b.Length); i++)
        {
            var aNumeric = IsNumeric(a[i]);
            var bNumeric = IsNumeric(b[i]);
            var order = (aNumeric, bNumeric) switch
            {
                // Lengths first: numeric identifiers have no leading zeros and may exceed int.
                (true, true) => a[i].Length != b[i].Length
                    ? a[i].Length.CompareTo(b[i].Length)
                    : string.CompareOrdinal(a[i], b[i]),
                (true, false) => -1,
                (false, true) => 1,
                (false, false) => string.CompareOrdinal(a[i], b[i]),
            };
            if (order != 0)
            {
                return Math.Sign(order);
            }
        }

        return a.Length.CompareTo(b.Length);
    }

    private static bool TryParseNumber(string part, out int value)
    {
        value = 0;
        return IsNumeric(part)
            && (part.Length == 1 || part[0] != '0')
            && int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    private static bool AreValidIdentifiers(string text, bool allowLeadingZeros) =>
        text.Split('.').All(id =>
            id.Length > 0
            && id.All(c => char.IsAsciiLetterOrDigit(c) || c == '-')
            && (allowLeadingZeros || !IsNumeric(id) || id.Length == 1 || id[0] != '0'));

    private static bool IsNumeric(string id) => id.Length > 0 && id.All(char.IsAsciiDigit);
}

internal sealed class SemVersionJsonConverter : JsonConverter<SemVersion>
{
    public override SemVersion Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.String && SemVersion.TryParse(reader.GetString(), out var version)
            ? version
            : throw new JsonException("Expected a SemVer string.");

    public override void Write(Utf8JsonWriter writer, SemVersion value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}
