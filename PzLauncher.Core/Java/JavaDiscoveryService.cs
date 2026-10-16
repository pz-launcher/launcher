using Microsoft.Extensions.Logging;
using PzLauncher.Core.Common;
using PzLauncher.Core.Platform;

namespace PzLauncher.Core.Java;

internal sealed partial class JavaDiscoveryService(
    IPlatformEnvironment platform,
    ILogger<JavaDiscoveryService> logger) : IJavaDiscoveryService
{
    // Bounds link resolution, so a symlink loop can't hang the scan.
    private const int MaxLinkPasses = 40;

    // Tests point these at a temp directory; the real ones are absolute Linux paths.
    internal IReadOnlyList<string> LinuxSystemFolders { get; init; } = JavaLocations.LinuxSystemFolders;

    private string ExecutableName => JavaLocations.ExecutableName(platform.Os);

    public async Task<Result<JavaScanResult>> ScanAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var warnings = new List<Error>();
        if (platform.Os == PlatformKind.MacOS)
        {
            return new JavaScanResult([], warnings);
        }

        var comparer = StringComparer.FromComparison(platform.PathComparison);
        var seen = new HashSet<string>(comparer);
        var runtimes = new List<JavaRuntime>();
        foreach (var (home, source) in CollectCandidates(warnings, ct))
        {
            ct.ThrowIfCancellationRequested();
            var canonical = Canonicalize(home);
            if (!seen.Add(canonical))
            {
                continue;
            }

            var runtime = await ReadRuntimeAsync(canonical, source, warnings, ct).ConfigureAwait(false);
            if (runtime.IsSuccess)
            {
                runtimes.Add(runtime.Value);
            }
        }

        var ordered = runtimes
            .OrderByDescending(r => r.Version?.Major ?? 0)
            .ThenBy(r => r.HomePath, comparer)
            .ToList();
        return new JavaScanResult(ordered, warnings);
    }

    public async Task<Result<JavaRuntime>> InspectAsync(string path, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!PathNormalizer.TryNormalizeAbsolute(Unquote(path), out var folder))
        {
            return JavaErrors.InvalidPath(path);
        }

        if (!Directory.Exists(folder))
        {
            return JavaErrors.PathNotFound(folder);
        }

        var home = folder;
        if (!File.Exists(ExecutableIn(folder))
            && string.Equals(Path.GetFileName(folder), JavaLocations.BinFolder, platform.PathComparison)
            && File.Exists(Path.Combine(folder, ExecutableName))
            && Path.GetDirectoryName(folder) is { } parent)
        {
            home = parent;
        }

        return await ReadRuntimeAsync(Canonicalize(home), JavaRuntimeSource.Manual, warnings: null, ct).ConfigureAwait(false);
    }

    // Exposed for tests: temp folders can themselves sit behind a link (macOS /var).
    internal static string ResolveRealPath(string path)
    {
        // ResolveLinkTarget only follows the last component, so walk every component; repeat because a
        // link target can itself have linked ancestors (/usr/lib64 -> /usr/lib).
        var current = path;
        for (var pass = 0; pass < MaxLinkPasses; pass++)
        {
            var next = ResolveComponents(current);
            if (string.Equals(next, current, StringComparison.Ordinal))
            {
                break;
            }

            current = next;
        }

        return current;
    }

    private static string ResolveComponents(string path)
    {
        var root = Path.GetPathRoot(path);
        if (string.IsNullOrEmpty(root))
        {
            return path;
        }

        var current = root;
        foreach (var part in path[root.Length..].Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
        {
            current = ResolveLink(new DirectoryInfo(Path.Combine(current, part)));
        }

        return current;
    }

    private static string ResolveLink(FileSystemInfo entry)
    {
        try
        {
            var target = entry.ResolveLinkTarget(returnFinalTarget: true);
            return PathNormalizer.Normalize(target?.FullName ?? entry.FullName);
        }
        catch (IOException)
        {
            return entry.FullName;
        }
        catch (UnauthorizedAccessException)
        {
            return entry.FullName;
        }
    }

    private static string? Unquote(string? value) => value?.Trim().Trim('"');

    private string ExecutableIn(string home) => Path.Combine(home, JavaLocations.BinFolder, ExecutableName);

    // Collapses link spellings of one home, and a JDK 8 <jdk>/jre into <jdk>.
    private string Canonicalize(string home)
    {
        var real = ResolveRealPath(home);
        return string.Equals(Path.GetFileName(real), JavaLocations.LegacyJreFolder, platform.PathComparison)
            && Path.GetDirectoryName(real) is { } jdk
            && File.Exists(ExecutableIn(jdk))
                ? jdk
                : real;
    }

    private List<(string Home, JavaRuntimeSource Source)> CollectCandidates(List<Error> warnings, CancellationToken ct)
    {
        var candidates = new List<(string Home, JavaRuntimeSource Source)>();

        void Add(string? raw, JavaRuntimeSource source)
        {
            if (PathNormalizer.TryNormalizeAbsolute(Unquote(raw), out var home))
            {
                candidates.Add((home, source));
            }
        }

        Add(platform.GetEnvironmentVariable("JAVA_HOME"), JavaRuntimeSource.JavaHome);

        foreach (var home in PathHomes())
        {
            Add(home, JavaRuntimeSource.Path);
        }

        if (platform.Os == PlatformKind.Windows)
        {
            foreach (var home in RegistryHomes(warnings, ct))
            {
                Add(home, JavaRuntimeSource.Registry);
            }
        }

        foreach (var folder in KnownFolders())
        {
            ct.ThrowIfCancellationRequested();
            foreach (var home in SubFolders(folder, warnings))
            {
                Add(home, JavaRuntimeSource.KnownFolder);
            }
        }

        return candidates;
    }

    // A PATH entry holding the executable: follow the executable's link (/usr/bin/java -> alternatives -> real file), take <bin>/..
    private IEnumerable<string> PathHomes()
    {
        var path = platform.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path))
        {
            yield break;
        }

        foreach (var entry in path.Split(JavaLocations.PathListSeparator(platform.Os), StringSplitOptions.RemoveEmptyEntries))
        {
            if (!PathNormalizer.TryNormalizeAbsolute(Unquote(entry), out var dir))
            {
                continue;
            }

            var executable = Path.Combine(dir, ExecutableName);
            if (!File.Exists(executable))
            {
                continue;
            }

            var real = ResolveLink(new FileInfo(executable));
            if (Path.GetDirectoryName(Path.GetDirectoryName(real)) is { } home)
            {
                yield return home;
            }
        }
    }

    private List<string> RegistryHomes(List<Error> warnings, CancellationToken ct)
    {
        var homes = new List<string>();
        foreach (var hive in JavaLocations.RegistryHives)
        {
            foreach (var location in JavaLocations.Registry)
            {
                ct.ThrowIfCancellationRequested();
                IReadOnlyList<string> subKeys;
                try
                {
                    subKeys = platform.GetRegistrySubKeyNames(hive, location.Key);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    AddWarning(warnings, JavaErrors.SourceUnreadable(RegistryDisplayPath(hive, location.Key), ex.Message));
                    continue;
                }

                foreach (var subKey in subKeys)
                {
                    var keyPath = location.Suffix is null
                        ? $@"{location.Key}\{subKey}"
                        : $@"{location.Key}\{subKey}\{location.Suffix}";
                    try
                    {
                        if (platform.ReadRegistryString(hive, keyPath, location.ValueName) is { } value)
                        {
                            homes.Add(value);
                        }
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        AddWarning(warnings, JavaErrors.SourceUnreadable(RegistryDisplayPath(hive, keyPath), ex.Message));
                    }
                }
            }
        }

        return homes;
    }

    private static string RegistryDisplayPath(RegistryHiveKind hive, string path) => hive switch
    {
        RegistryHiveKind.LocalMachine => $@"HKLM\{path}",
        RegistryHiveKind.CurrentUser => $@"HKCU\{path}",
    };

    private List<string> KnownFolders()
    {
        var folders = new List<string>();
        var home = Path.IsPathFullyQualified(platform.HomeDirectory) ? platform.HomeDirectory : null;
        switch (platform.Os)
        {
            case PlatformKind.Windows:
                var programFiles = Unquote(platform.GetEnvironmentVariable("ProgramFiles"));
                if (PathNormalizer.TryNormalizeAbsolute(programFiles, out var programFilesRoot))
                {
                    folders.AddRange(JavaLocations.WindowsProgramFilesFolders.Select(name => Path.Combine(programFilesRoot, name)));
                }

                break;
            case PlatformKind.Linux:
                folders.AddRange(LinuxSystemFolders);
                var sdkman = Unquote(platform.GetEnvironmentVariable(JavaLocations.SdkmanDirVariable));
                if (PathNormalizer.TryNormalizeAbsolute(sdkman, out var sdkmanDir))
                {
                    folders.Add(JavaLocations.SdkmanCandidates(sdkmanDir));
                }
                else if (home is not null)
                {
                    folders.Add(JavaLocations.SdkmanCandidates(Path.Combine(home, JavaLocations.DefaultSdkmanDir)));
                }

                break;
            case PlatformKind.MacOS:
                break;
        }

        // An empty home (no profile) would make these relative to the working directory.
        if (home is not null)
        {
            folders.AddRange(JavaLocations.HomeFolders.Select(name => Path.Combine(home, name)));
        }

        return folders;
    }

    // A missing folder is normal; one that exists but can't be listed is a warning.
    private List<string> SubFolders(string folder, List<Error> warnings)
    {
        try
        {
            return Directory.Exists(folder) ? [.. Directory.EnumerateDirectories(folder).Order(StringComparer.Ordinal)] : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AddWarning(warnings, JavaErrors.SourceUnreadable(folder, ex.Message));
            return [];
        }
    }

    private async Task<Result<JavaRuntime>> ReadRuntimeAsync(
        string home, JavaRuntimeSource source, List<Error>? warnings, CancellationToken ct)
    {
        var executable = ExecutableIn(home);
        if (!File.Exists(executable))
        {
            return JavaErrors.NotAJavaHome(home);
        }

        var release = await ReadReleaseAsync(home, warnings, ct).ConfigureAwait(false);
        var arch = release.Arch != JavaArch.Unknown
            ? release.Arch
            : await JavaExecutableHeader.ReadArchAsync(executable, ct).ConfigureAwait(false);

        return arch switch
        {
            JavaArch.Bits64 => new JavaRuntime(home, executable, release.Version, release.Vendor, source, release.HasGraalCompiler),
            JavaArch.Bits32 => JavaErrors.Not64Bit(home),
            JavaArch.Unknown => JavaErrors.NotAJavaHome(home),
        };
    }

    // Missing is normal (unknown version); unreadable is a warning during a scan.
    private async Task<JavaReleaseInfo> ReadReleaseAsync(string home, List<Error>? warnings, CancellationToken ct)
    {
        var path = Path.Combine(home, JavaLocations.ReleaseFile);
        try
        {
            return JavaReleaseFile.Parse(await File.ReadAllTextAsync(path, ct).ConfigureAwait(false));
        }
        catch (FileNotFoundException)
        {
            return JavaReleaseInfo.Empty;
        }
        catch (DirectoryNotFoundException)
        {
            return JavaReleaseInfo.Empty;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            var error = JavaErrors.ReleaseUnreadable(path, ex.Message);
            if (warnings is null)
            {
                LogWarning(logger, error.Code.ToWireName(), error.Message);
            }
            else
            {
                AddWarning(warnings, error);
            }

            return JavaReleaseInfo.Empty;
        }
    }

    private void AddWarning(List<Error> warnings, Error warning)
    {
        LogWarning(logger, warning.Code.ToWireName(), warning.Message);
        warnings.Add(warning);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Java discovery: {Code}: {Detail}")]
    private static partial void LogWarning(ILogger logger, string code, string detail);
}
