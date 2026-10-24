using PzLauncher.Core.Platform;
using PzLauncher.Core.Update.Releases;
using PzLauncher.Core.Update;

namespace PzLauncher.Core.Steam.LaunchHook;

/// <summary>
/// The launcher's hook in an app's Steam launch options: <c>"&lt;launcher exe&gt;" %command%</c>, which makes Steam start
/// the launcher with the game's command line as its arguments. Everything else in the options is the user's.
/// </summary>
internal static class SteamLaunchOptions
{
    private const string Command = SteamClientFiles.CommandPlaceholder;

    private static readonly string HookFileName = LauncherReleases.ExecutableName(PlatformKind.Windows);

    /// <summary>The first hook's launcher path; <c>null</c> when the options have none.</summary>
    public static string? FindHookPath(string options, string launcherExe) => Find(options, launcherExe)?.Path;

    /// <summary>Re-points an existing hook, else puts one before the first <c>%command%</c>, else in front of the options.</summary>
    public static string AddHook(string options, string launcherExe)
    {
        var quoted = $"\"{launcherExe}\" ";
        if (Find(options, launcherExe) is { } hook)
        {
            return string.Concat(options.AsSpan(0, hook.Start), quoted, options.AsSpan(hook.CommandStart));
        }

        var command = options.IndexOf(Command, StringComparison.Ordinal);
        if (command >= 0)
        {
            return string.Concat(options.AsSpan(0, command), quoted, options.AsSpan(command));
        }

        // Options without %command% are appended to the game's command line, so they keep that place after it.
        return options.Trim().Length == 0 ? quoted + Command : $"{quoted}{Command} {options}";
    }

    /// <summary>Undoes <see cref="AddHook"/>: options it put after a leading <c>%command%</c> mean the same without it.</summary>
    public static string RemoveHook(string options, string launcherExe)
    {
        var result = options;
        while (Find(result, launcherExe) is { } hook)
        {
            var leading = result.AsSpan(0, hook.Start).IsWhiteSpace();
            result = string.Concat(result.AsSpan(0, hook.Start), result.AsSpan(hook.CommandStart));
            if (leading)
            {
                result = WithoutLeadingCommand(result);
            }
        }

        return result;
    }

    private static string WithoutLeadingCommand(string options)
    {
        var trimmed = options.TrimStart();
        return trimmed.StartsWith(Command, StringComparison.Ordinal)
               && (trimmed.Length == Command.Length || char.IsWhiteSpace(trimmed[Command.Length]))
            ? trimmed[Command.Length..].TrimStart()
            : options;
    }

    private static Hook? Find(string options, string launcherExe)
    {
        for (var start = options.IndexOf('"'); start >= 0;)
        {
            var end = options.IndexOf('"', start + 1);
            if (end < 0)
            {
                return null;
            }

            var command = end + 1;
            while (command < options.Length && char.IsWhiteSpace(options[command]))
            {
                command++;
            }

            var path = options[(start + 1)..end];
            if (command > end + 1
                && options.AsSpan(command).StartsWith(Command, StringComparison.Ordinal)
                && IsLauncherPath(path, launcherExe))
            {
                return new Hook(start, command, path);
            }

            start = options.IndexOf('"', end + 1);
        }

        return null;
    }

    // Windows paths parsed by hand, so a hook written on Windows reads the same on any host.
    private static bool IsLauncherPath(string path, string launcherExe)
    {
        var rooted = path.StartsWith('\\') || path.StartsWith('/')
            || (path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] is '\\' or '/');
        var name = FileName(path);
        return rooted
               && (name.Equals(HookFileName, StringComparison.OrdinalIgnoreCase)
                   || name.Equals(FileName(launcherExe), StringComparison.OrdinalIgnoreCase));
    }

    private static string FileName(string path) => path[(path.LastIndexOfAny(['\\', '/']) + 1)..];

    /// <param name="Start">The opening quote.</param>
    /// <param name="CommandStart">The <c>%command%</c> after it.</param>
    private sealed record Hook(int Start, int CommandStart, string Path);
}
