using PzLauncher.Core.Platform;

namespace PzLauncher.Core.ZombieBuddy;

// Agent options are "key=value" items after the first '=', joined by ',' (ZombieBuddy Agent.java:premain).
internal static class ZombieBuddyAgentArgs
{
    private const string PolicyKey = "policy";

    private const string AllowAllPolicy = "allow-all";

    private const char OptionSeparator = ',';

    public static bool IsAgentArg(string arg, PlatformKind os) => Matches(arg, ZombieBuddyFiles.AgentArg(os));

    public static bool IsAnyAgentArg(string arg) => ZombieBuddyFiles.AllAgentArgs.Any(agent => Matches(arg, agent));

    public static bool Has(IReadOnlyList<string> vmArgs, PlatformKind os) => vmArgs.Any(arg => IsAgentArg(arg, os));

    public static bool HasAny(IReadOnlyList<string> vmArgs) => vmArgs.Any(IsAnyAgentArg);

    /// <summary>Prepends the bare agent arg unless one, with or without options, is already there.</summary>
    public static IReadOnlyList<string> Add(IReadOnlyList<string> vmArgs, PlatformKind os) =>
        Has(vmArgs, os) ? vmArgs : [ZombieBuddyFiles.AgentArg(os), .. vmArgs];

    public static IReadOnlyList<string> Remove(IReadOnlyList<string> vmArgs) =>
        HasAny(vmArgs) ? [.. vmArgs.Where(arg => !IsAnyAgentArg(arg))] : vmArgs;

    // ZombieBuddy lower-cases keys and trims and lower-cases the policy; a repeated key's last value wins.
    public static bool IsAllowAll(IReadOnlyList<string> vmArgs, PlatformKind os)
    {
        if (vmArgs.FirstOrDefault(arg => IsAgentArg(arg, os)) is not { } agent)
        {
            return false;
        }

        var policy = Options(agent, os).LastOrDefault(IsPolicy);
        return policy is not null
            && string.Equals(policy[(policy.IndexOf('=', StringComparison.Ordinal) + 1)..].Trim(), AllowAllPolicy, StringComparison.OrdinalIgnoreCase);
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
            if (!IsPolicy(option))
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

        var agent = ZombieBuddyFiles.AgentArg(os);
        var rewritten = kept.Count == 0 ? agent : $"{agent}={string.Join(OptionSeparator, kept)}";
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

    private static bool IsPolicy(string option)
    {
        var separator = option.IndexOf('=', StringComparison.Ordinal);
        var key = separator < 0 ? option : option[..separator];
        return string.Equals(key, PolicyKey, StringComparison.OrdinalIgnoreCase);
    }
}
