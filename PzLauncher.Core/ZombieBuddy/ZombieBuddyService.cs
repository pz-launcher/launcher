using Microsoft.Extensions.Logging;
using PzLauncher.Core.Common;
using PzLauncher.Core.Installs;
using PzLauncher.Core.Launch;
using PzLauncher.Core.Platform;
using PzLauncher.Core.Steam;

namespace PzLauncher.Core.ZombieBuddy;

internal sealed partial class ZombieBuddyService(
    IInstallDetectionService installs,
    SteamLocator locator,
    SteamLibraryReader reader,
    IPlatformEnvironment platform,
    ILogger<ZombieBuddyService> logger) : IZombieBuddyService
{
    // Concurrent operations would otherwise interleave their file copies and json rewrites.
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<Result<ZombieBuddyStatus>> GetStatusAsync(string installRoot, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var install = await installs.InspectAsync(installRoot, ct).ConfigureAwait(false);
        if (!install.IsSuccess)
        {
            return install.Error;
        }

        if (!CheckSupported(install.Value).IsSuccess)
        {
            return ZombieBuddyStatus.Unsupported;
        }

        var game = PzLaunchFiles.ResolveGameFolder(install.Value.RootPath);
        var config = await PzLaunchConfigEditor.LoadAsync(Path.Combine(game, PzLaunchFiles.ConfigFile), ct).ConfigureAwait(false);
        if (!config.IsSuccess)
        {
            return Logged(config.Error);
        }

        var os = platform.Os;
        var vmArgs = config.Value.VmArgs;
        var subscribed = await FindItemsAsync(install.Value.RootPath, ct).ConfigureAwait(false);
        var initialized = ZombieBuddyFiles.RequiredFiles(os).All(file => File.Exists(Path.Combine(game, file)))
            && ZombieBuddyAgentArgs.Has(vmArgs, os);
        var leftovers = ZombieBuddyFiles.LeftoverFiles.Any(file => File.Exists(Path.Combine(game, file)))
            || ZombieBuddyAgentArgs.HasAny(vmArgs);
        var action = subscribed.Count > 0
            ? initialized ? ZombieBuddyAction.None : ZombieBuddyAction.Initialize
            : leftovers ? ZombieBuddyAction.Remove : ZombieBuddyAction.None;
        return new ZombieBuddyStatus(true, subscribed, initialized, leftovers, ZombieBuddyAgentArgs.IsAllowAll(vmArgs, os), action);
    }

    public async Task<Result> InitializeAsync(
        string installRoot, ZombieBuddyChannel channel, IProgress<ZombieBuddyProgress>? progress = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var target = await ResolveAsync(installRoot, ct).ConfigureAwait(false);
        if (!target.IsSuccess)
        {
            return target.Error;
        }

        var (root, game) = target.Value;
        var os = platform.Os;
        var libs = "";
        var steps = new List<Step>
        {
            new(ZombieBuddyStep.FindItem, async stepCt =>
            {
                var item = await FindItemAsync(root, channel, stepCt).ConfigureAwait(false);
                if (!item.IsSuccess)
                {
                    return item.Error;
                }

                libs = ZombieBuddyFiles.LibsFolder(item.Value);
                var missing = ZombieBuddyFiles.RequiredFiles(os).Select(file => Path.Combine(libs, file)).FirstOrDefault(path => !File.Exists(path));
                return missing is null ? Result.Ok() : ZombieBuddyErrors.SourceFileMissing(missing);
            }),
        };
        foreach (var file in ZombieBuddyFiles.RequiredFiles(os))
        {
            steps.Add(new(CopyStep(file), _ => Task.FromResult(Copy(Path.Combine(libs, file), Path.Combine(game, file)))));
        }

        steps.Add(new(ZombieBuddyStep.AddAgentArg, stepCt => EditVmArgsAsync(game, args => ZombieBuddyAgentArgs.Add(args, os), stepCt)));
        return await RunAsync(steps, progress, ct).ConfigureAwait(false);
    }

    public async Task<Result> RemoveAsync(string installRoot, IProgress<ZombieBuddyProgress>? progress = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var target = await ResolveAsync(installRoot, ct).ConfigureAwait(false);
        if (!target.IsSuccess)
        {
            return target.Error;
        }

        var game = target.Value.GameFolder;
        Step[] steps =
        [
            new(ZombieBuddyStep.DeleteFiles, _ => Task.FromResult(DeleteLeftovers(game))),
            new(ZombieBuddyStep.RemoveAgentArg, stepCt => EditVmArgsAsync(game, ZombieBuddyAgentArgs.Remove, stepCt)),
        ];
        return await RunAsync(steps, progress, ct).ConfigureAwait(false);
    }

    public async Task<Result> SetAllowAllAsync(string installRoot, bool allowAll, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var target = await ResolveAsync(installRoot, ct).ConfigureAwait(false);
        if (!target.IsSuccess)
        {
            return target.Error;
        }

        var os = platform.Os;
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var config = await PzLaunchConfigEditor.LoadAsync(Path.Combine(target.Value.GameFolder, PzLaunchFiles.ConfigFile), ct).ConfigureAwait(false);
            if (!config.IsSuccess)
            {
                return Logged(config.Error);
            }

            if (!ZombieBuddyAgentArgs.Has(config.Value.VmArgs, os))
            {
                return Logged(ZombieBuddyErrors.NotInitialized(config.Value.FilePath));
            }

            var saved = await config.Value.SaveVmArgsAsync(ZombieBuddyAgentArgs.WithPolicy(config.Value.VmArgs, os, allowAll), ct).ConfigureAwait(false);
            return saved.IsSuccess ? saved : Logged(saved.Error);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<Result<ZabricPreload>> EnsureZabricPreloadAsync(string installRoot, string extraGameArgs, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        // The launch that follows reports a bad install or json itself.
        var target = await ResolveAsync(installRoot, ct).ConfigureAwait(false);
        if (!target.IsSuccess)
        {
            return ZabricPreload.NotApplicable;
        }

        var (root, game) = target.Value;
        var os = platform.Os;
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var config = await PzLaunchConfigEditor.LoadAsync(Path.Combine(game, PzLaunchFiles.ConfigFile), ct).ConfigureAwait(false);
            if (!config.IsSuccess || !ZombieBuddyAgentArgs.IsAllowAll(config.Value.VmArgs, os))
            {
                return ZabricPreload.NotApplicable;
            }

            var libraries = await CandidateLibrariesAsync(root, ct).ConfigureAwait(false);
            var cache = ZabricLocator.CacheFolder(extraGameArgs, game, platform.HomeDirectory);
            if (await ZabricLocator.FindAsync(cache, libraries, logger, ct).ConfigureAwait(false) is not { } zabric
                || ConfigFolder(config.Value.VmArgs, game) is not { } configFolder)
            {
                return ZabricPreload.NotApplicable;
            }

            var entry = await zabric.CheckPreloadableAsync(ct).ConfigureAwait(false);
            if (!entry.IsSuccess)
            {
                return Logged(entry.Error);
            }

            var result = await ZombieBuddyConfigEditor.EnsureZabricAsync(configFolder, entry.Value, platform.PathComparison, ct).ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                return Logged(result.Error);
            }

            if (result.Value == ZabricPreload.Written)
            {
                LogZabricWritten(logger, Path.Combine(configFolder, ZabricFiles.ConfigFile), entry.Value.JarPath);
            }

            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static ZombieBuddyStep CopyStep(string file) => file switch
    {
        ZombieBuddyFiles.NativeLoader => ZombieBuddyStep.CopyNativeLoader,
        ZombieBuddyFiles.AgentJar => ZombieBuddyStep.CopyAgentJar,
        _ => throw new ArgumentOutOfRangeException(nameof(file), file, "Not a required ZombieBuddy file."),
    };

    private Result CheckSupported(PzInstall install)
    {
        if (platform.Os == PlatformKind.MacOS)
        {
            return LaunchErrors.PlatformUnsupported(platform.Os);
        }

        return install.Kind switch
        {
            PzInstallKind.Game => Result.Ok(),
            PzInstallKind.DedicatedServer => LaunchErrors.NotAGame(install.RootPath),
        };
    }

    // ZombieBuddy's Agent.configDir; a relative config_dir resolves against the game's working folder.
    private string? ConfigFolder(IReadOnlyList<string> vmArgs, string game)
    {
        if (ZombieBuddyAgentArgs.Option(vmArgs, platform.Os, ZabricFiles.ConfigDirOption) is { Length: > 0 } configured)
        {
            return PathNormalizer.TryNormalizeAgainst(configured, game, out var folder) ? folder : null;
        }

        return PathNormalizer.TryNormalizeAbsolute(platform.HomeDirectory, out var home) ? Path.Combine(home, ZabricFiles.DefaultConfigFolder) : null;
    }

    private async Task<Result<Target>> ResolveAsync(string installRoot, CancellationToken ct)
    {
        var install = await installs.InspectAsync(installRoot, ct).ConfigureAwait(false);
        if (!install.IsSuccess)
        {
            return install.Error;
        }

        var supported = CheckSupported(install.Value);
        return supported.IsSuccess
            ? new Target(install.Value.RootPath, PzLaunchFiles.ResolveGameFolder(install.Value.RootPath))
            : supported.Error;
    }

    private async Task<Result> RunAsync(IReadOnlyList<Step> steps, IProgress<ZombieBuddyProgress>? progress, CancellationToken ct)
    {
        IReadOnlyList<ZombieBuddyStep> names = [.. steps.Select(step => step.Name)];
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            for (var i = 0; i < steps.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                progress?.Report(new ZombieBuddyProgress(names, i, IsComplete: false));
                var result = await steps[i].Run(ct).ConfigureAwait(false);
                if (!result.IsSuccess)
                {
                    return Logged(result.Error);
                }
            }

            progress?.Report(new ZombieBuddyProgress(names, names.Count, IsComplete: true));
            return Result.Ok();
        }
        finally
        {
            _gate.Release();
        }
    }

    private static async Task<Result> EditVmArgsAsync(
        string game, Func<IReadOnlyList<string>, IReadOnlyList<string>> edit, CancellationToken ct)
    {
        var config = await PzLaunchConfigEditor.LoadAsync(Path.Combine(game, PzLaunchFiles.ConfigFile), ct).ConfigureAwait(false);
        if (!config.IsSuccess)
        {
            return config.Error;
        }

        return await config.Value.SaveVmArgsAsync(edit(config.Value.VmArgs), ct).ConfigureAwait(false);
    }

    private static Result Copy(string source, string destination)
    {
        try
        {
            File.Copy(source, destination, overwrite: true);
            return Result.Ok();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ZombieBuddyErrors.CopyFailed(destination, ex.Message);
        }
    }

    private static Result DeleteLeftovers(string game)
    {
        foreach (var file in ZombieBuddyFiles.LeftoverFiles)
        {
            var path = Path.Combine(game, file);
            try
            {
                File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return ZombieBuddyErrors.DeleteFailed(path, ex.Message);
            }
        }

        return Result.Ok();
    }

    private async Task<Result<string>> FindItemAsync(string installRoot, ZombieBuddyChannel channel, CancellationToken ct)
    {
        var libraries = await CandidateLibrariesAsync(installRoot, ct).ConfigureAwait(false);
        var folder = libraries.Select(library => ZombieBuddyFiles.ItemFolder(library, channel)).FirstOrDefault(Directory.Exists);
        return folder is null ? ZombieBuddyErrors.NotSubscribed(ZombieBuddyFiles.ItemId(channel)) : folder;
    }

    private async Task<List<ZombieBuddyItem>> FindItemsAsync(string installRoot, CancellationToken ct)
    {
        var libraries = await CandidateLibrariesAsync(installRoot, ct).ConfigureAwait(false);
        var items = new List<ZombieBuddyItem>();
        foreach (var channel in ZombieBuddyFiles.Channels)
        {
            var folder = libraries.Select(library => ZombieBuddyFiles.ItemFolder(library, channel)).FirstOrDefault(Directory.Exists);
            if (folder is not null)
            {
                items.Add(new ZombieBuddyItem(channel, folder));
            }
        }

        return items;
    }

    // The installer's order: the game's own library, then each Steam root and the libraries it lists.
    private async Task<List<string>> CandidateLibrariesAsync(string installRoot, CancellationToken ct)
    {
        var seen = new HashSet<string>(StringComparer.FromComparison(platform.PathComparison));
        var libraries = new List<string>();

        void Add(string? path)
        {
            if (PathNormalizer.TryNormalizeAbsolute(path, out var normalized) && seen.Add(normalized))
            {
                libraries.Add(normalized);
            }
        }

        Add(OwnLibrary(installRoot));
        foreach (var root in locator.FindSteamRoots())
        {
            ct.ThrowIfCancellationRequested();
            Add(root);
            var listed = await reader.ReadLibraryPathsAsync(root, ct).ConfigureAwait(false);
            if (!listed.IsSuccess)
            {
                LogIgnored(logger, listed.Error.Code.ToWireName(), listed.Error.Message);
                continue;
            }

            foreach (var library in listed.Value)
            {
                Add(library);
            }
        }

        return libraries;
    }

    // A Steam install root is <library>/steamapps/common/<installdir>; a manual folder has no own library.
    private string? OwnLibrary(string installRoot)
    {
        var common = Path.GetDirectoryName(installRoot);
        var steamapps = Path.GetDirectoryName(common);
        return common is not null
            && steamapps is not null
            && string.Equals(Path.GetFileName(common), "common", platform.PathComparison)
            && string.Equals(Path.GetFileName(steamapps), "steamapps", platform.PathComparison)
                ? Path.GetDirectoryName(steamapps)
                : null;
    }

    private Error Logged(Error error)
    {
        LogWarning(logger, error.Code.ToWireName(), error.Message);
        return error;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "ZombieBuddy: {Code}: {Detail}")]
    private static partial void LogWarning(ILogger logger, string code, string detail);

    [LoggerMessage(Level = LogLevel.Information, Message = "ZombieBuddy: Zabric preload written to {ConfigPath}: {JarPath}")]
    private static partial void LogZabricWritten(ILogger logger, string configPath, string jarPath);

    [LoggerMessage(Level = LogLevel.Debug, Message = "ZombieBuddy: Steam library list ignored: {Code}: {Detail}")]
    private static partial void LogIgnored(ILogger logger, string code, string detail);

    /// <param name="GameFolder">The folder that holds the json; Linux nests it below the root.</param>
    private sealed record Target(string RootPath, string GameFolder);

    private sealed record Step(ZombieBuddyStep Name, Func<CancellationToken, Task<Result>> Run);
}
