using System.Formats.Tar;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using PzLauncher.Core.Net;
using PzLauncher.Core.Platform;
using PzLauncher.Core.Update;

namespace PzLauncher.Core.Tests.TestSupport;

internal static class UpdateTestKit
{
    public const string GitHubLatest = "https://api.github.com/repos/pz-launcher/launcher/releases/latest";
    public const string GiteeLatest = "https://gitee.com/api/v5/repos/dontcampy/pzl/releases/latest";
    public const string PackageUrl = "https://example.test/download/package";

    public static SemVersion V(string text) =>
        SemVersion.TryParse(text, out var version) ? version : throw new ArgumentException($"Not a version: {text}", nameof(text));

    /// <summary>The real v0.2.0 response, retagged to <paramref name="version"/>.</summary>
    public static string GitHubJson(string version = "0.2.0") => Fixtures.Read("Update", "github-latest.json").Replace("0.2.0", version, StringComparison.Ordinal);

    public static string GiteeJson(string version = "0.2.0") => Fixtures.Read("Update", "gitee-latest.json").Replace("0.2.0", version, StringComparison.Ordinal);

    public static UpdateService Service(
        FakeHttpHandler handler,
        string installDirectory,
        string current = "0.2.0",
        PlatformKind os = PlatformKind.Windows,
        Action<string, string>? moveFile = null,
        TimeSpan? timeout = null,
        Architecture architecture = Architecture.X64,
        FakeProcessRunner? processes = null) =>
        new(
            V(current),
            new LauncherHttpClient(handler, V(current)),
            new FakePlatformEnvironment { Os = os },
            processes ?? new FakeProcessRunner(),
            new ManualTimeProvider(),
            NullLogger<UpdateService>.Instance)
        {
            InstallDirectory = installDirectory,
            ProcessArchitecture = architecture,
            RequestTimeout = timeout ?? TimeSpan.FromSeconds(30),
            StallTimeout = timeout ?? TimeSpan.FromSeconds(30),
            MoveFile = moveFile ?? ((source, target) => File.Move(source, target)),
        };

    public static ReleaseInfo Release(byte[] package, string version = "0.3.0", string rid = "win-x64", bool withChecksum = true) =>
        new(
            V(version),
            withChecksum ? UpdateSource.GitHub : UpdateSource.Gitee,
            $"PzLauncher-{version}-{rid}{(rid.StartsWith("win", StringComparison.Ordinal) ? ".zip" : ".tar.gz")}",
            new Uri(PackageUrl),
            withChecksum ? package.Length : null,
            withChecksum ? Convert.ToHexStringLower(SHA256.HashData(package)) : null,
            new Uri($"https://github.com/pz-launcher/launcher/releases/tag/v{version}"));

    /// <summary>Entries go under the package folder, as <c>release.yml</c> packs them; <c>..</c> names stay as given.</summary>
    public static byte[] Zip(params (string Name, string Content)[] entries)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                using var entry = archive.CreateEntry(InPackage(name)).Open();
                entry.Write(Encoding.UTF8.GetBytes(content));
            }
        }

        return stream.ToArray();
    }

    /// <summary>Entry names as <c>tar -C publish PzLauncher</c> writes them; <c>..</c> names stay as given.</summary>
    public static byte[] TarGz(params (string Name, string Content)[] entries)
    {
        using var stream = new MemoryStream();
        using (var gzip = new GZipStream(stream, CompressionLevel.Fastest, leaveOpen: true))
        using (var tar = new TarWriter(gzip, TarEntryFormat.Pax))
        {
            tar.WriteEntry(new PaxTarEntry(TarEntryType.Directory, LauncherReleases.PackageFolder + "/"));
            foreach (var (name, content) in entries)
            {
                var entry = new PaxTarEntry(TarEntryType.RegularFile, InPackage(name))
                {
                    DataStream = new MemoryStream(Encoding.UTF8.GetBytes(content)),
                    Mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
                };
                tar.WriteEntry(entry);
            }
        }

        return stream.ToArray();
    }

    private static string InPackage(string name) =>
        name.StartsWith("..", StringComparison.Ordinal) ? name : LauncherReleases.PackageFolder + "/" + name;

    /// <summary>Every file (relative path → content) and directory below <paramref name="root"/>.</summary>
    public static (SortedDictionary<string, string> Files, SortedSet<string> Directories) Snapshot(string root) =>
        (
            new SortedDictionary<string, string>(
                Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                    .ToDictionary(path => Path.GetRelativePath(root, path), File.ReadAllText),
                StringComparer.Ordinal),
            new SortedSet<string>(
                Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories).Select(path => Path.GetRelativePath(root, path)),
                StringComparer.Ordinal));

    public static string ReadText(string path) => File.ReadAllText(path);

    public static void WriteFile(string root, string relative, string content)
    {
        var path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? root);
        File.WriteAllText(path, content);
    }
}
