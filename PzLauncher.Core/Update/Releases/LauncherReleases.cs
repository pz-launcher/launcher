using System.Runtime.InteropServices;
using PzLauncher.Core.Platform;

namespace PzLauncher.Core.Update.Releases;

/// <summary>Where releases are published and how their packages are named and staged.</summary>
internal static class LauncherReleases
{
    public static readonly ReleaseFeedEndpoint GitHub = new(
        UpdateSource.GitHub,
        new Uri("https://api.github.com/repos/pz-launcher/launcher/releases/latest"),
        "https://github.com/pz-launcher/launcher/releases/tag/");

    // Mirrored by .github/workflows/gitee-sync.yml; publishes no size or checksum.
    public static readonly ReleaseFeedEndpoint Gitee = new(
        UpdateSource.Gitee,
        new Uri("https://gitee.com/api/v5/repos/dontcampy/pzl/releases/latest"),
        "https://gitee.com/dontcampy/pzl/releases/tag/");

    public const string ProductName = "PzLauncher";

    /// <summary>The one top-level folder of a package (<c>release.yml</c>); its contents mirror the install folder.</summary>
    public const string PackageFolder = ProductName;

    public const string Sha256DigestPrefix = "sha256:";

    // All under the install folder, so moves between them and the install are renames on one volume.
    public const string UpdateFolder = ".update";
    public const string DownloadFolder = "download";
    public const string StagedFolder = "staged";
    public const string OldFolder = "old";
    public const string PartialSuffix = ".part";
    public const string ReadyMarkerSuffix = ".ready";

    /// <summary>Only the platforms <c>release.yml</c> publishes; <c>null</c> for any other.</summary>
    public static string? RuntimeIdentifier(PlatformKind os, Architecture architecture) =>
        architecture != Architecture.X64
            ? null
            : os switch
            {
                PlatformKind.Windows => "win-x64",
                PlatformKind.Linux => "linux-x64",
                PlatformKind.MacOS => null,
            };

    public static string PackageName(SemVersion version, string rid) =>
        $"{ProductName}-{version}-{rid}{(rid.StartsWith("win-", StringComparison.Ordinal) ? ".zip" : ".tar.gz")}";

    public static bool IsZip(string packageName) => packageName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);

    public static string ExecutableName(PlatformKind os) =>
        os switch
        {
            PlatformKind.Windows => ProductName + ".exe",
            PlatformKind.MacOS or PlatformKind.Linux => ProductName,
        };
}

/// <param name="ReleasePagePrefix">The tag is appended to get the release page.</param>
internal sealed record ReleaseFeedEndpoint(UpdateSource Source, Uri LatestReleaseUrl, string ReleasePagePrefix);
