using PzLauncher.Core.Platform;

namespace PzLauncher.Core.Mod.ZombieBuddy;

// Agent options are "key=value" items after the first '=', joined by ',' (ZombieBuddy Agent.java:premain).
internal static class ZombieBuddyAgentArgs
{
    private const string PolicyKey = "policy";

    private const string AllowAllPolicy = "allow-all";

    private const char OptionSeparator = ',';

    // ZombieBuddy Agent.java: patches_jar=<path>:<package>[;<path>:<package>...].
    private const string PatchesJarKey = "patches_jar";

    private const char EntrySeparator = ';';

    private const char PatchSeparator = ':';

    public static bool IsAgentArg(string arg, PlatformKind os) => Matches(arg, ZombieBuddyFiles.AgentArg(os));

    public static bool IsAnyAgentArg(string arg) => ZombieBuddyFiles.AllAgentArgs.Any(agent => Matches(arg, agent));

    public static bool Has(IReadOnlyList<string> vmArgs, PlatformKind os) => vmArgs.Any(arg => IsAgentArg(arg, os));

    public static bool HasAny(IReadOnlyList<string> vmArgs) => vmArgs.Any(IsAnyAgentArg);

    /// <summary>Prepends the bare agent arg unless one, with or without options, is already there.</summary>
    public static IReadOnlyList<string> Add(IReadOnlyList<string> vmArgs, PlatformKind os) =>
        Has(vmArgs, os) ? vmArgs : [ZombieBuddyFiles.AgentArg(os), .. vmArgs];

    public static IReadOnlyList<string> Remove(IReadOnlyList<string> vmArgs) =>
        HasAny(vmArgs) ? [.. vmArgs.Where(arg => !IsAnyAgentArg(arg))] : vmArgs;

    // ZombieBuddy trims and lower-cases the policy.
    public static bool IsAllowAll(IReadOnlyList<string> vmArgs, PlatformKind os) =>
        string.Equals(Option(vmArgs, os, PolicyKey), AllowAllPolicy, StringComparison.OrdinalIgnoreCase);

    // ZombieBuddy lower-cases keys; a repeated key's last value wins.
    /// <summary>The trimmed value of <paramref name="key"/> on the first agent arg; empty for a bare key, <c>null</c> when absent.</summary>
    public static string? Option(IReadOnlyList<string> vmArgs, PlatformKind os, string key)
    {
        if (vmArgs.FirstOrDefault(arg => IsAgentArg(arg, os)) is not { } agent
            || Options(agent, os).LastOrDefault(option => IsKey(option, key)) is not { } option)
        {
            return null;
        }

        var separator = option.IndexOf('=', StringComparison.Ordinal);
        return separator < 0 ? "" : option[(separator + 1)..].Trim();
    }

    /// <summary>Rewrites only the <c>policy</c> option of the first agent arg; the other options keep their order.</summary>
    public static IReadOnlyList<string> WithPolicy(IReadOnlyList<string> vmArgs, PlatformKind os, bool allowAll)
    {
        var index = IndexOfAgent(vmArgs, os);
        if (index < 0)
        {
            return vmArgs;
        }

        var options = Options(vmArgs[index], os);
        var kept = new List<string>(options.Count + 1);
        var placed = false;
        foreach (var option in options)
        {
            if (!IsKey(option, PolicyKey))
            {
                kept.Add(option);
            }
            else if (allowAll && !placed)
            {
                kept.Add($"{PolicyKey}={AllowAllPolicy}");
                placed = true;
            }
        }

        if (allowAll && !placed)
        {
            kept.Add($"{PolicyKey}={AllowAllPolicy}");
        }

        return Rewrite(vmArgs, os, index, kept);
    }

    /// <summary>
    /// Makes the first agent arg's <c>patches_jar</c> hold one <c>&lt;path&gt;:&lt;package&gt;</c> entry for the package:
    /// the first one replaced in place, any others dropped, else appended. Unchanged args come back as the same instance.
    /// </summary>
    public static IReadOnlyList<string> WithPatchesJar(IReadOnlyList<string> vmArgs, PlatformKind os, string path, string packageName) =>
        EditPatchesJar(vmArgs, os, packageName, $"{path}{PatchSeparator}{packageName}");

    /// <summary>Drops the package's <c>patches_jar</c> entries, and the option when none is left. Unchanged args come back as the same instance.</summary>
    public static IReadOnlyList<string> WithoutPatchesJar(IReadOnlyList<string> vmArgs, PlatformKind os, string packageName) =>
        EditPatchesJar(vmArgs, os, packageName, entry: null);

    // ZombieBuddy reads only the last patches_jar option, so the earlier ones are left as they are.
    private static IReadOnlyList<string> EditPatchesJar(IReadOnlyList<string> vmArgs, PlatformKind os, string packageName, string? entry)
    {
        var index = IndexOfAgent(vmArgs, os);
        if (index < 0)
        {
            return vmArgs;
        }

        var options = Options(vmArgs[index], os);
        var at = options.FindLastIndex(option => IsKey(option, PatchesJarKey));
        if (at < 0)
        {
            if (entry is null)
            {
                return vmArgs;
            }

            options.Add($"{PatchesJarKey}={entry}");
            return Rewrite(vmArgs, os, index, options);
        }

        var option = options[at];
        var separator = option.IndexOf('=', StringComparison.Ordinal);
        string[] entries = separator < 0 ? [] : option[(separator + 1)..].Split(EntrySeparator, StringSplitOptions.RemoveEmptyEntries);
        var kept = new List<string>(entries.Length + 1);
        var placed = false;
        foreach (var existing in entries)
        {
            if (!IsPackageEntry(existing, packageName))
            {
                kept.Add(existing);
            }
            else if (entry is { } own && !placed)
            {
                kept.Add(own);
                placed = true;
            }
        }

        if (entry is { } added && !placed)
        {
            kept.Add(added);
        }
        else if (entry is null && kept.Count == entries.Length)
        {
            return vmArgs;
        }

        // Dropping the last option would hand ZombieBuddy an earlier one; left empty, it keeps those unread.
        if (kept.Count == 0 && !options.Take(at).Any(earlier => IsKey(earlier, PatchesJarKey)))
        {
            options.RemoveAt(at);
        }
        else
        {
            options[at] = $"{(separator < 0 ? option : option[..separator])}={string.Join(EntrySeparator, kept)}";
        }

        return Rewrite(vmArgs, os, index, options);
    }

    // The text after the last ':' fits both ZombieBuddy split rules: on the first ':' up to 2.3.x, on the last since 3.0.
    private static bool IsPackageEntry(string entry, string packageName)
    {
        var separator = entry.LastIndexOf(PatchSeparator);
        return separator >= 0 && string.Equals(entry[(separator + 1)..].Trim(), packageName, StringComparison.Ordinal);
    }

    private static IReadOnlyList<string> Rewrite(IReadOnlyList<string> vmArgs, PlatformKind os, int index, List<string> options)
    {
        var agent = ZombieBuddyFiles.AgentArg(os);
        var rewritten = options.Count == 0 ? agent : $"{agent}={string.Join(OptionSeparator, options)}";
        if (string.Equals(rewritten, vmArgs[index], StringComparison.Ordinal))
        {
            return vmArgs;
        }

        var result = vmArgs.ToList();
        result[index] = rewritten;
        return result;
    }

    private static bool Matches(string arg, string agent) =>
        arg.StartsWith(agent, StringComparison.Ordinal) && (arg.Length == agent.Length || arg[agent.Length] == '=');

    private static int IndexOfAgent(IReadOnlyList<string> vmArgs, PlatformKind os)
    {
        for (var i = 0; i < vmArgs.Count; i++)
        {
            if (IsAgentArg(vmArgs[i], os))
            {
                return i;
            }
        }

        return -1;
    }

    private static List<string> Options(string agentArg, PlatformKind os)
    {
        var agent = ZombieBuddyFiles.AgentArg(os);
        return agentArg.Length <= agent.Length + 1
            ? []
            : [.. agentArg[(agent.Length + 1)..].Split(OptionSeparator, StringSplitOptions.RemoveEmptyEntries)];
    }

    private static bool IsKey(string option, string key)
    {
        var separator = option.IndexOf('=', StringComparison.Ordinal);
        return string.Equals(separator < 0 ? option : option[..separator], key, StringComparison.OrdinalIgnoreCase);
    }
}
