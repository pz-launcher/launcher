using System.Globalization;
using System.Text;
using PzLauncher.Core.Java;
using PzLauncher.Core.Platform;

namespace PzLauncher.Core.Launch;

internal static class LaunchArguments
{
    public const string MaxHeapPrefix = "-Xmx";
    public const string InitialHeapPrefix = "-Xms";

    private const string ClasspathOption = "-cp";

    /// <summary>JVM args, <c>-cp</c>, main class and game args, in the order the JVM expects them.</summary>
    public static List<string> Build(PzLaunchConfig config, PlatformKind os, Version osVersion, GameLaunchOverrides overrides)
    {
        var jvmArgs = WithHeapSize(config.JvmArgs(os, osVersion), MaxHeapPrefix, overrides.MaxMemoryMb);
        jvmArgs = WithHeapSize(jvmArgs, InitialHeapPrefix, overrides.MinMemoryMb);
        return
        [
            .. jvmArgs,
            .. Split(overrides.ExtraJvmArgs),
            ClasspathOption,
            string.Join(JavaLocations.PathListSeparator(os), config.Classpath),
            config.MainClass.Replace('/', '.'),
            .. Split(overrides.ExtraGameArgs),
        ];
    }

    /// <summary>Whitespace separates; double quotes group and are removed; an unclosed quote runs to the end. No escapes.</summary>
    public static List<string> Split(string text)
    {
        var args = new List<string>();
        var current = new StringBuilder();
        var inToken = false;
        var inQuotes = false;
        foreach (var c in text)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
                inToken = true;
            }
            else if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (inToken)
                {
                    args.Add(current.ToString());
                    current.Clear();
                    inToken = false;
                }
            }
            else
            {
                current.Append(c);
                inToken = true;
            }
        }

        if (inToken)
        {
            args.Add(current.ToString());
        }

        return args;
    }

    /// <summary>The last <paramref name="prefix"/> option wins, as in the JVM; <c>null</c> when absent or unparseable.</summary>
    public static int? HeapSizeMb(IReadOnlyList<string> jvmArgs, string prefix)
    {
        for (var i = jvmArgs.Count - 1; i >= 0; i--)
        {
            if (jvmArgs[i].StartsWith(prefix, StringComparison.Ordinal))
            {
                return ParseSizeMb(jvmArgs[i][prefix.Length..]);
            }
        }

        return null;
    }

    /// <summary>JVM size syntax: bytes with an optional <c>k</c>/<c>m</c>/<c>g</c> suffix, any case; rounded down to whole MB.</summary>
    public static int? ParseSizeMb(string size)
    {
        if (size.Length == 0)
        {
            return null;
        }

        var (digits, bytesPerUnit) = char.ToLowerInvariant(size[^1]) switch
        {
            'k' => (size[..^1], 1024L),
            'm' => (size[..^1], 1024L * 1024),
            'g' => (size[..^1], 1024L * 1024 * 1024),
            _ => (size, 1L),
        };
        if (!long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var count)
            || count > long.MaxValue / bytesPerUnit)
        {
            return null;
        }

        var mb = count * bytesPerUnit / (1024 * 1024);
        return mb is > 0 and <= int.MaxValue ? (int)mb : null;
    }

    private static IReadOnlyList<string> WithHeapSize(IReadOnlyList<string> jvmArgs, string prefix, int? sizeMb) =>
        sizeMb is { } mb
            ? [.. jvmArgs.Where(a => !a.StartsWith(prefix, StringComparison.Ordinal)), string.Create(CultureInfo.InvariantCulture, $"{prefix}{mb}m")]
            : jvmArgs;
}
