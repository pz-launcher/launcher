using Microsoft.Extensions.Logging;
using PzLauncher.Core.Common;
using PzLauncher.Core.Installs;
using PzLauncher.Core.Launch.Config;
using PzLauncher.Core.Launch;
using PzLauncher.Core.Platform;
using PzLauncher.Core.Steam;

namespace PzLauncher.Core.Mod.ZombieBuddy;

internal sealed partial class ZombieBuddyService(
    IInstallDetectionService installs,
    WorkshopLibraries workshop,
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
        return new ZombieBuddyStatus(
            true, subscribed, initialized, leftovers, ZombieBuddyAgentArgs.IsAllowAll(vmArgs, os), ConfigFolder(vmArgs, game), action);
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

    public async Task<Result> EnsurePatchesJarAsync(
        string installRoot, string sourceJar, string jarFile, string packageName, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var target = await ResolveAsync(installRoot, ct).ConfigureAwait(false);
        if (!target.IsSuccess)
        {
            return target.Error;
        }

        var game = target.Value.GameFolder;
        var os = platform.Os;
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var config = await PzLaunchConfigEditor.LoadAsync(Path.Combine(game, PzLaunchFiles.ConfigFile), ct).ConfigureAwait(false);
            if (!config.IsSuccess)
            {
                return Logged(config.Error);
            }

            // Nothing would load the jar; the launch reports the missing setup.
            if (!ZombieBuddyAgentArgs.Has(config.Value.VmArgs, os))
            {
                return Result.Ok();
            }

            // Copied first, so the entry never names a jar that isn't there. An unreadable copy counts as different:
            // the copy then reports why it can't be replaced.
            var jar = Path.Combine(game, jarFile);
            if (!await FileContent.TryEqualAsync(sourceJar, jar, ct).ConfigureAwait(false))
            {
                var copied = Copy(sourceJar, jar);
                if (!copied.IsSuccess)
                {
                    return Logged(copied.Error);
                }

                LogCopied(logger, sourceJar, jar);
            }

            return await SavePatchesJarAsync(
                config.Value, ZombieBuddyAgentArgs.WithPatchesJar(config.Value.VmArgs, os, jarFile, packageName), packageName, ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<Result> RemovePatchesJarAsync(string installRoot, string jarFile, string packageName, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var target = await ResolveAsync(installRoot, ct).ConfigureAwait(false);
        if (!target.IsSuccess)
        {
            return target.Error;
        }

        var game = target.Value.GameFolder;
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var config = await PzLaunchConfigEditor.LoadAsync(Path.Combine(game, PzLaunchFiles.ConfigFile), ct).ConfigureAwait(false);
            if (!config.IsSuccess)
            {
                return Logged(config.Error);
            }

            // The entry goes first, so it never names a jar that isn't there.
            var saved = await SavePatchesJarAsync(
                config.Value, ZombieBuddyAgentArgs.WithoutPatchesJar(config.Value.VmArgs, platform.Os, packageName), packageName, ct).ConfigureAwait(false);
            if (!saved.IsSuccess)
            {
                return saved;
            }

            var jar = Path.Combine(game, jarFile);
            if (!File.Exists(jar))
            {
                return Result.Ok();
            }

            try
            {
                File.Delete(jar);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return Logged(ZombieBuddyErrors.DeleteFailed(jar, ex.Message));
            }

            LogDeleted(logger, jar);
            return Result.Ok();
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
        if (ZombieBuddyAgentArgs.Option(vmArgs, platform.Os, ZombieBuddyFiles.ConfigDirOption) is { Length: > 0 } configured)
        {
            return PathNormalizer.TryNormalizeAgainst(configured, game, out var folder) ? folder : null;
        }

        return PathNormalizer.TryNormalizeAbsolute(platform.HomeDirectory, out var home) ? Path.Combine(home, ZombieBuddyFiles.DefaultConfigFolder) : null;
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

    private async Task<Result> SavePatchesJarAsync(PzLaunchConfigEditor config, IReadOnlyList<string> vmArgs, string packageName, CancellationToken ct)
    {
        if (ReferenceEquals(vmArgs, config.VmArgs))
        {
            return Result.Ok();
        }

        var saved = await config.SaveVmArgsAsync(vmArgs, ct).ConfigureAwait(false);
        if (!saved.IsSuccess)
        {
            return Logged(saved.Error);
        }

        LogPatchesJarSaved(logger, packageName, config.FilePath);
        return saved;
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
        var libraries = await workshop.CandidatesAsync(installRoot, ct).ConfigureAwait(false);
        var folder = libraries.Select(library => ZombieBuddyFiles.ItemFolder(library, channel)).FirstOrDefault(Directory.Exists);
        return folder is null ? ZombieBuddyErrors.NotSubscribed(ZombieBuddyFiles.ItemId(channel)) : folder;
    }

    private async Task<List<ZombieBuddyItem>> FindItemsAsync(string installRoot, CancellationToken ct)
    {
        var libraries = await workshop.CandidatesAsync(installRoot, ct).ConfigureAwait(false);
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

    private Error Logged(Error error)
    {
        LogWarning(logger, error.Code.ToWireName(), error.Message);
        return error;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "ZombieBuddy: {Code}: {Detail}")]
    private static partial void LogWarning(ILogger logger, string code, string detail);

    [LoggerMessage(Level = LogLevel.Information, Message = "ZombieBuddy: copied {SourcePath} to {Path}")]
    private static partial void LogCopied(ILogger logger, string sourcePath, string path);

    [LoggerMessage(Level = LogLevel.Information, Message = "ZombieBuddy: deleted {Path}")]
    private static partial void LogDeleted(ILogger logger, string path);

    [LoggerMessage(Level = LogLevel.Information, Message = "ZombieBuddy: patches_jar for {PackageName} updated in {ConfigPath}")]
    private static partial void LogPatchesJarSaved(ILogger logger, string packageName, string configPath);

    /// <param name="GameFolder">The folder that holds the json; Linux nests it below the root.</param>
    private sealed record Target(string RootPath, string GameFolder);

    private sealed record Step(ZombieBuddyStep Name, Func<CancellationToken, Task<Result>> Run);
}
