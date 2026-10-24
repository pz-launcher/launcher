using System.ComponentModel;
using System.Globalization;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using PzLauncher.Core.Common;
using PzLauncher.Core.Installs;
using PzLauncher.Core.Java;
using PzLauncher.Core.Launch.Command;
using PzLauncher.Core.Launch;
using PzLauncher.Core.Platform.Processes;
using PzLauncher.Core.Platform;
using PzLauncher.Core.Server.Config;
using PzLauncher.Core.Server.Rcon;

namespace PzLauncher.Core.Server;

internal sealed partial class ServerHostService(
    IInstallDetectionService installs,
    IPlatformEnvironment platform,
    IProcessRunner processes,
    IRconClient rcon,
    TimeProvider time,
    ILogger<ServerHostService> logger) : IServerHostService
{
    private const string PasswordChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";
    private const int PasswordLength = 24;

    public Task<Result<IReadOnlyList<NamedServer>>> ListServersAsync(ServerSite site, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var paths = SitePaths.Of(site);
        var names = ListNames(paths.CacheDir);
        return Task.FromResult<Result<IReadOnlyList<NamedServer>>>(
            names.IsSuccess ? names.Value.ConvertAll(name => Describe(paths.CacheDir, name)) : Failed(names.Error));
    }

    public async Task<Result<NamedServer>> CreateServerAsync(ServerSite site, string name, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var paths = SitePaths.Of(site);
        if (!ServerName.IsValidNew(name))
        {
            return ServerErrors.NameInvalid(name);
        }

        var changeGate = Memory(paths).ChangeGate;
        await changeGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var names = ListNames(paths.CacheDir);
            if (!names.IsSuccess)
            {
                return Failed(names.Error);
            }

            // Case-insensitively: the name becomes file names, and Windows folders ignore case.
            if (names.Value.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                return ServerErrors.AlreadyExists(name);
            }

            var ini = PzServerFiles.IniFile(paths.CacheDir, name);
            var content = ServerIniFile.Create(
            [
                new(PzServerFiles.RconPortKey, PzServerFiles.DefaultRconPort.ToString(CultureInfo.InvariantCulture)),
                new(PzServerFiles.RconPasswordKey, NewPassword()),
            ]);
            try
            {
                Directory.CreateDirectory(Path.Combine(paths.CacheDir, PzServerFiles.ServerFolder));
                await AtomicFile.WriteAsync(ini, content, ct, overwrite: false).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return Failed(ServerErrors.FileAccessFailed(ini, ex.Message));
            }

            return Describe(paths.CacheDir, name);
        }
        finally
        {
            changeGate.Release();
        }
    }

    public async Task<Result<ServerStatus>> StartAsync(StartServerRequest request, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var paths = SitePaths.Of(request.Site);
        if (platform.Os == PlatformKind.MacOS)
        {
            return LaunchErrors.PlatformUnsupported(platform.Os);
        }

        if (!ServerName.IsFileName(request.Name))
        {
            return ServerErrors.NameInvalid(request.Name);
        }

        if (request.MaxMemoryMb is int memory and <= 0)
        {
            return LaunchErrors.InvalidMemory(memory);
        }

        var changeGate = Memory(paths).ChangeGate;
        await changeGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Any server, not just this one: they share the cache folder's console file and usually the ports.
            if ((await ObserveAsync(paths, ct).ConfigureAwait(false)).Alive is { } running)
            {
                return ServerErrors.AlreadyRunning(running.Name);
            }

            var plan = await PlanAsync(paths, request, ct).ConfigureAwait(false);
            return plan.IsSuccess ? await LaunchAsync(paths, plan.Value, ct).ConfigureAwait(false) : Failed(plan.Error);
        }
        finally
        {
            changeGate.Release();
        }
    }

    private Result<List<string>> ListNames(string cacheDir)
    {
        var folder = Path.Combine(cacheDir, PzServerFiles.ServerFolder);
        try
        {
            List<string> names = Directory.Exists(folder)
                ?
                [
                    .. Directory.EnumerateFiles(folder, "*" + PzServerFiles.IniExtension)
                        .Where(file => string.Equals(Path.GetExtension(file), PzServerFiles.IniExtension, platform.PathComparison))
                        .Select(Path.GetFileNameWithoutExtension)
                        .OfType<string>()
                        .Order(StringComparer.OrdinalIgnoreCase),
                ]
                : [];
            return names;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ServerErrors.FileAccessFailed(folder, ex.Message);
        }
    }

    private static NamedServer Describe(string cacheDir, string name) =>
        new(name, Directory.Exists(PzServerFiles.WorldFolder(cacheDir, name)), File.Exists(PzServerFiles.DatabaseFile(cacheDir, name)));

    private async Task<Result<StartPlan>> PlanAsync(SitePaths paths, StartServerRequest request, CancellationToken ct)
    {
        var install = await installs.InspectAsync(paths.InstallRoot, ct).ConfigureAwait(false);
        if (!install.IsSuccess)
        {
            return install.Error;
        }

        var root = install.Value.RootPath;
        var isServer = install.Value.Kind switch
        {
            PzInstallKind.DedicatedServer => true,
            PzInstallKind.Game => false,
        };
        if (!isServer)
        {
            return ServerErrors.NotAServerInstall(root);
        }

        var name = request.Name;
        var read = await ServerIniFile.ReadAsync(PzServerFiles.IniFile(paths.CacheDir, name), ct).ConfigureAwait(false);
        if (read is null)
        {
            return ServerErrors.NotFound(name);
        }

        if (!read.Value.IsSuccess)
        {
            return read.Value.Error;
        }

        // PZ would prompt on stdin for it, which is the null device. A failed first start leaves a database without an admin;
        // the status then reports this error from the console.
        var adminPassword = string.IsNullOrEmpty(request.AdminPassword) ? null : request.AdminPassword;
        if (adminPassword is null && !File.Exists(PzServerFiles.DatabaseFile(paths.CacheDir, name)))
        {
            return ServerErrors.AdminPasswordRequired(name);
        }

        var game = PzLaunchFiles.ResolveGameFolder(root);
        var command = await LaunchCommand.ReadAsync(game, ct).ConfigureAwait(false);
        if (!command.IsSuccess)
        {
            return command.Error;
        }

        var javaHome = LaunchCommand.BundledJavaHome(game);
        var java = LaunchCommand.FindJava(javaHome, JavaLocations.ConsoleExecutableName(platform.Os));
        if (!java.IsSuccess)
        {
            return java.Error;
        }

        var ini = read.Value.Value;
        var (rconPort, rconPassword) = EnsureRcon(ini);
        var overrides = GameLaunchOverrides.Empty with { MaxMemoryMb = request.MaxMemoryMb };
        var start = command.Value.Start(
            platform,
            java.Value,
            javaHome,
            JvmArgs(command.Value, overrides),
            overrides,
            GameArgs(name, paths.CacheDir, adminPassword),
            LinuxEnvironment);
        return new StartPlan(name, root, ini, rconPort, rconPassword, start);
    }

    // RCON is off without a password, and the host needs it to see the server get ready, to send commands and to stop it.
    private static (int Port, string Password) EnsureRcon(ServerIniFile ini)
    {
        if (!int.TryParse(ini.Get(PzServerFiles.RconPortKey), NumberStyles.None, CultureInfo.InvariantCulture, out var port)
            || port is < 1 or > 65535)
        {
            port = PzServerFiles.DefaultRconPort;
            ini.Set(PzServerFiles.RconPortKey, port.ToString(CultureInfo.InvariantCulture));
        }

        var password = ini.Get(PzServerFiles.RconPasswordKey);
        if (string.IsNullOrEmpty(password))
        {
            password = NewPassword();
            ini.Set(PzServerFiles.RconPasswordKey, password);
        }

        return (port, password);
    }

    private List<string> JvmArgs(LaunchCommand command, GameLaunchOverrides overrides)
    {
        var jvmArgs = LaunchArguments.JvmArgs(command.JsonJvmArgs(platform), overrides, []);
        if (!jvmArgs.Any(arg => arg == PzServerFiles.RconLoopbackProperty
            || arg.StartsWith(PzServerFiles.RconLoopbackProperty + "=", StringComparison.Ordinal)))
        {
            jvmArgs.Add(PzServerFiles.RconLoopbackArg);
        }

        return jvmArgs;
    }

    private List<string> GameArgs(string name, string cacheDir, string? adminPassword)
    {
        List<string> args = [PzServerFiles.ServerNameArg, name];
        if (!IsDefaultCacheDir(cacheDir))
        {
            args.Add(PzServerFiles.CacheDirArgPrefix + cacheDir);
        }

        if (adminPassword is not null)
        {
            args.AddRange([PzServerFiles.AdminPasswordArg, adminPassword]);
        }

        return args;
    }

    private bool IsDefaultCacheDir(string cacheDir) =>
        PathNormalizer.TryNormalizeAbsolute(Path.Combine(platform.HomeDirectory, PzServerFiles.DefaultCacheFolder), out var defaultDir)
        && string.Equals(defaultDir, cacheDir, platform.PathComparison);

    // Mirrors start-server.sh, not the game's projectzomboid.sh: linux64 first, and neither the game's thread preload nor its
    // XMODIFIERS fix.
    private static Dictionary<string, string> LinuxEnvironment(
        string gameFolder, string javaHome, string? inheritedLibraryPath, string? inheritedPreload) => new(StringComparer.Ordinal)
        {
            [LinuxLaunchEnvironment.LibraryPathVariable] = LinuxLaunchEnvironment.JoinNonEmpty(
                Path.Combine(gameFolder, PzServerFiles.LinuxNativesFolder),
                Path.Combine(gameFolder, PzLaunchFiles.NativesFolder),
                gameFolder,
                Path.Combine(javaHome, PzLaunchFiles.JavaLibFolder),
                inheritedLibraryPath),
            [LinuxLaunchEnvironment.PreloadVariable] = LinuxLaunchEnvironment.JoinNonEmpty([inheritedPreload, .. PzServerFiles.LinuxPreloadLibraries]),
        };

    private async Task<Result<ServerStatus>> LaunchAsync(SitePaths paths, StartPlan plan, CancellationToken ct)
    {
        var ini = await plan.Ini.WriteAsync(ct).ConfigureAwait(false);
        if (!ini.IsSuccess)
        {
            return Failed(ini.Error);
        }

        DetachedProcess process;
        try
        {
            process = processes.StartDetached(plan.Start);
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException)
        {
            return Failed(ServerErrors.StartFailed(plan.Start.FileName, ex.Message));
        }

        var running = new RunningServer(
            process.Id, process.StartTime, plan.Name, plan.InstallRoot, paths.CacheDir, plan.RconPort, plan.RconPassword, Ready: false, StopRequestedAt: null);

        // The process runs: from here it is recorded or ended, whatever the caller's token says.
        var site = Memory(paths);
        await site.FileGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            try
            {
                await WriteStateFileAsync(paths.RunningFile, running, RunningServerJsonContext.Default.RunningServer, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                KillUnrecorded(process.Id);
                return Failed(ServerErrors.FileAccessFailed(paths.RunningFile, ex.Message));
            }

            await SetLastFailureAsync(paths, site, null).ConfigureAwait(false);
        }
        finally
        {
            site.FileGate.Release();
        }

        ResetProbe(site);
        LogStarted(logger, plan.Name, process.Id);
        return StatusOf(running);
    }

    private void KillUnrecorded(int processId)
    {
        try
        {
            processes.KillTree(processId);
        }
        catch (Win32Exception ex)
        {
            LogKillFailed(logger, processId, ex.Message);
        }
    }

    private static string NewPassword() => RandomNumberGenerator.GetString(PasswordChars, PasswordLength);

    private Error Failed(Error error)
    {
        if (error.Kind is ErrorKind.External or ErrorKind.Unexpected)
        {
            LogFailed(logger, error.Code.ToWireName(), error.Message);
        }

        return error;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Server host: {Code}: {Detail}")]
    private static partial void LogFailed(ILogger logger, string code, string detail);

    [LoggerMessage(Level = LogLevel.Information, Message = "Server {Name} started as process {ProcessId}")]
    private static partial void LogStarted(ILogger logger, string name, int processId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Server {Name} stopped")]
    private static partial void LogStopped(ILogger logger, string name);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Server process {ProcessId} could not be killed: {Detail}")]
    private static partial void LogKillFailed(ILogger logger, int processId, string detail);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Server state file {Path}: {Detail}")]
    private static partial void LogStateFileProblem(ILogger logger, string path, string detail);

    /// <param name="CacheDir">Absolute and normalized, like <paramref name="RunningFile"/>.</param>
    /// <param name="RunningFile">Also the key of the site's locks and memory.</param>
    private sealed record SitePaths(string InstallRoot, string CacheDir, string RunningFile, string LastFailureFile)
    {
        private const string StateFolder = "server";
        private const string RunningFileName = "running.json";
        private const string LastFailureFileName = "last-failure.json";

        // The host's own settings: a relative path there is a bug, not user input.
        public static SitePaths Of(ServerSite site)
        {
            var state = Path.Combine(Absolute(site.StateDir), StateFolder);
            return new(site.InstallRoot, Absolute(site.CacheDir), Path.Combine(state, RunningFileName), Path.Combine(state, LastFailureFileName));
        }

        private static string Absolute(string path) =>
            PathNormalizer.TryNormalizeAbsolute(path, out var normalized)
                ? normalized
                : throw new ArgumentException("A server site's folders must be absolute paths.", nameof(path));
    }

    private sealed record StartPlan(
        string Name, string InstallRoot, ServerIniFile Ini, int RconPort, string RconPassword, ProcessStartRequest Start);
}
