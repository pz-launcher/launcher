using PzLauncher.Core.Platform;
using PzLauncher.Core.Steam;
using PzLauncher.Core.Tests.TestSupport;

namespace PzLauncher.Core.Tests.Steam;

// Paths are built from a temp directory so they're absolute on every host OS.
public sealed class SteamLocatorTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void GetCandidateRoots_Windows_RegistryFirstThenProgramFiles()
    {
        var registry = Path.Combine(_temp.Path, "RegSteam");
        var programFiles = Path.Combine(_temp.Path, "PF86");
        var env = new FakePlatformEnvironment { Os = PlatformKind.Windows, SteamRegistryPath = registry };
        env.EnvironmentVariables["ProgramFiles(x86)"] = programFiles;

        var roots = new SteamLocator(env).GetCandidateRoots();

        Assert.Equal([registry, Path.Combine(programFiles, "Steam")], roots);
    }

    [Fact]
    public void GetCandidateRoots_WindowsSameRootDifferentCasingAndSlashes_Deduplicated()
    {
        var programFiles = Path.Combine(_temp.Path, "PF86");
        var steam = Path.Combine(programFiles, "Steam");
        var env = new FakePlatformEnvironment
        {
            Os = PlatformKind.Windows,
            PathComparison = StringComparison.OrdinalIgnoreCase,
            SteamRegistryPath = steam.ToUpperInvariant().Replace('\\', '/') + "/",
        };
        env.EnvironmentVariables["ProgramFiles(x86)"] = programFiles;

        var roots = new SteamLocator(env).GetCandidateRoots();

        Assert.Single(roots);
    }

    [Fact]
    public void GetCandidateRoots_WindowsNothingConfigured_ReturnsEmpty()
    {
        var env = new FakePlatformEnvironment { Os = PlatformKind.Windows };

        Assert.Empty(new SteamLocator(env).GetCandidateRoots());
    }

    [Fact]
    public void GetCandidateRoots_WindowsRelativeRegistryValue_IsIgnored()
    {
        var env = new FakePlatformEnvironment { Os = PlatformKind.Windows, SteamRegistryPath = "relative/steam" };

        Assert.Empty(new SteamLocator(env).GetCandidateRoots());
    }

    [Fact]
    public void GetCandidateRoots_MacOS_UsesApplicationSupport()
    {
        var env = new FakePlatformEnvironment { Os = PlatformKind.MacOS, HomeDirectory = _temp.Path, PathComparison = StringComparison.Ordinal };

        var roots = new SteamLocator(env).GetCandidateRoots();

        Assert.Equal([Path.Combine(_temp.Path, "Library", "Application Support", "Steam")], roots);
    }

    [Fact]
    public void GetCandidateRoots_Linux_ReturnsNativeThenXdgThenFlatpak()
    {
        var env = new FakePlatformEnvironment { Os = PlatformKind.Linux, HomeDirectory = _temp.Path, PathComparison = StringComparison.Ordinal };

        var roots = new SteamLocator(env).GetCandidateRoots();

        Assert.Equal(
            [
                Path.Combine(_temp.Path, ".steam", "steam"),
                Path.Combine(_temp.Path, ".local", "share", "Steam"),
                Path.Combine(_temp.Path, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam"),
            ],
            roots);
    }

    [Fact]
    public void GetCandidateRoots_LinuxIgnoresRegistry()
    {
        var env = new FakePlatformEnvironment
        {
            Os = PlatformKind.Linux,
            HomeDirectory = _temp.Path,
            SteamRegistryPath = Path.Combine(_temp.Path, "Reg"),
        };

        var roots = new SteamLocator(env).GetCandidateRoots();

        Assert.DoesNotContain(Path.Combine(_temp.Path, "Reg"), roots);
    }

    [Fact]
    public void FindSteamRoots_KeepsOnlyRootsWithSteamapps()
    {
        var env = new FakePlatformEnvironment { Os = PlatformKind.Linux, HomeDirectory = _temp.Path, PathComparison = StringComparison.Ordinal };
        var xdg = _temp.CreateDirectory(".local", "share", "Steam");
        Directory.CreateDirectory(Path.Combine(xdg, "steamapps"));
        _temp.CreateDirectory(".steam", "steam"); // exists but has no steamapps

        var roots = new SteamLocator(env).FindSteamRoots();

        Assert.Equal([xdg], roots);
    }

    [Fact]
    public void FindSteamRoots_NoCandidatesExist_ReturnsEmpty()
    {
        var env = new FakePlatformEnvironment { Os = PlatformKind.MacOS, HomeDirectory = _temp.Path };

        Assert.Empty(new SteamLocator(env).FindSteamRoots());
    }
}
