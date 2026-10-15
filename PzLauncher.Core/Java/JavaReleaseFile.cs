using System.Globalization;

namespace PzLauncher.Core.Java;

internal enum JavaArch
{
    Bits64,
    Bits32,
    Unknown,
}

internal sealed record JavaReleaseInfo(JavaVersion? Version, string? Vendor, JavaArch Arch)
{
    public static JavaReleaseInfo Empty { get; } = new(null, null, JavaArch.Unknown);
}

// The JDK's <home>/release file: KEY="value" lines; line endings vary within one file.
internal static class JavaReleaseFile
{
    public static JavaReleaseInfo Parse(string text)
    {
        string? version = null;
        string? vendor = null;
        string? arch = null;
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            var equals = line.IndexOf('=', StringComparison.Ordinal);
            if (equals <= 0)
            {
                continue;
            }

            var value = Unquote(line[(equals + 1)..]);
            switch (line[..equals].Trim())
            {
                case "JAVA_VERSION":
                    version = value;
                    break;
                case "IMPLEMENTOR":
                    vendor = value;
                    break;
                case "OS_ARCH":
                    arch = value;
                    break;
            }
        }

        return new JavaReleaseInfo(
            ParseVersion(version),
            string.IsNullOrWhiteSpace(vendor) ? null : vendor,
            ParseArch(arch));
    }

    // "1.8.0_392" is Java 8; from Java 9 on the first number is the major ("17", "21.0.9", "11.0.9.1").
    public static JavaVersion? ParseVersion(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var parts = text.Split('.');
        var major = parts.Length > 1 && parts[0] == "1" ? LeadingNumber(parts[1]) : LeadingNumber(parts[0]);
        return major is > 0 ? new JavaVersion(major.Value, text) : null;
    }

    // JDK 9+ writes OPENJDK_TARGET_CPU (x86_64, x86, aarch64); JDK 8 the legacy names (amd64, i586).
    public static JavaArch ParseArch(string? osArch) => osArch?.ToLowerInvariant() switch
    {
        "x86_64" or "amd64" or "aarch64" => JavaArch.Bits64,
        "x86" or "i386" or "i586" or "i686" => JavaArch.Bits32,
        _ => JavaArch.Unknown,
    };

    private static string Unquote(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"' ? trimmed[1..^1] : trimmed;
    }

    private static int? LeadingNumber(string text)
    {
        var digits = 0;
        while (digits < text.Length && char.IsAsciiDigit(text[digits]))
        {
            digits++;
        }

        return digits > 0 && int.TryParse(text.AsSpan(0, digits), NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            ? number
            : null;
    }
}
