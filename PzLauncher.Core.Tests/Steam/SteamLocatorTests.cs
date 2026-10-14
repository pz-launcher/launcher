using PzLauncher.Core.Platform;
using PzLauncher.Core.Steam;
using PzLauncher.Core.Tests.TestSupport;

namespace PzLauncher.Core.Tests.Steam;

// Paths are built from a temp directory so they're absolute on every host OS.
public sealed class SteamLocatorTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private FakePlatformEnvironment Env(PlatformKind os, string? registry = null, string? programFiles = null)
    {
        var env = new FakePlatformEnvironment
        {
            Os = os,
            HomeDirectory = _temp.Path,
            SteamRegistryPath = registry,
            PathComparison = os == PlatformKind.Windows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal,
        };
        if (programFiles is not null)
        {
            env.EnvironmentVariables["ProgramFiles(x86)"] = programFiles;
        }

        return env;
    }

    [Fact]
    public void GetCandidateRoots_Windows_RegistryFirstThenProgramFiles()
    {
        var registry = Path.Combine(_temp.Path, "RegSteam");
        var programFiles = Path.Combine(_temp.Path, "PF86");

        var roots = new SteamLocator(Env(PlatformKind.Windows, registry, programFiles)).GetCandidateRoots();

        Assert.Equal([registry, Path.Combine(programFiles, "Steam")], roots);
    }

    [Fact]
    public void GetCandidateRoots_WindowsSameRootDifferentCasingAndSlashes_Deduplicated()
    {
        var programFiles = Path.Combine(_temp.Path, "PF86");
        var registry = Path.Combine(programFiles, "Steam").ToUpperInvariant().Replace('\\', '/') + "/";

        Assert.Single(new SteamLocator(Env(PlatformKind.Windows, registry, programFiles)).GetCandidateRoots());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("relative/steam")]
    public void GetCandidateRoots_WindowsNoUsableRegistryOrProgramFiles_ReturnsEmpty(string? registry) =>
        Assert.Empty(new SteamLocator(Env(PlatformKind.Windows, registry)).GetCandidateRoots());

    [Fact]
    public void GetCandidateRoots_MacOS_UsesApplicationSupport()
    {
        var roots = new SteamLocator(Env(PlatformKind.MacOS)).GetCandidateRoots();

        Assert.Equal([Path.Combine(_temp.Path, "Library", "Application Support", "Steam")], roots);
    }

    [Fact]
    public void GetCandidateRoots_Linux_ReturnsNativeThenXdgThenFlatpakIgnoringRegistry()
    {
        var roots = new SteamLocator(Env(PlatformKind.Linux, registry: Path.Combine(_temp.Path, "Reg"))).GetCandidateRoots();

        Assert.Equal(
            [
                Path.Combine(_temp.Path, ".steam", "steam"),
                Path.Combine(_temp.Path, ".local", "share", "Steam"),
                Path.Combine(_temp.Path, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam"),
            ],
            roots);
    }

    [Fact]
    public void FindSteamRoots_KeepsOnlyExistingRootsWithSteamapps()
    {
        var xdg = _temp.CreateDirectory(".local", "share", "Steam");
        Directory.CreateDirectory(Path.Combine(xdg, "steamapps"));
        _temp.CreateDirectory(".steam", "steam"); // exists but has no steamapps; the flatpak root doesn't exist

        Assert.Equal([xdg], new SteamLocator(Env(PlatformKind.Linux)).FindSteamRoots());
    }
}
