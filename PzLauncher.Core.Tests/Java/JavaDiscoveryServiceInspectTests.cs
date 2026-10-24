using PzLauncher.Core.Common;
using PzLauncher.Core.Java;
using PzLauncher.Core.Platform;
using PzLauncher.Core.Tests.TestSupport;
using static PzLauncher.Core.Tests.TestSupport.Builders.JavaHomeBuilder;

namespace PzLauncher.Core.Tests.Java;

public sealed class JavaDiscoveryServiceInspectTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly string _root;
    private readonly JavaHomeBuilder _java;
    private readonly JavaDiscoveryService _service;

    public JavaDiscoveryServiceInspectTests()
    {
        _root = JavaDiscoveryService.ResolveRealPath(_temp.Path);
        _java = new JavaHomeBuilder(_root, PlatformKind.Windows);
        _service = JavaDiscoveryServiceScanTests.CreateService(new FakePlatformEnvironment
        {
            Os = PlatformKind.Windows,
            PathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal,
        });
    }

    public void Dispose() => _temp.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("relative/folder")]
    [InlineData("bad\0path")]
    public async Task InspectAsync_InvalidPath_ReturnsInvalidPath(string path) =>
        ResultAssert.Failed(await _service.InspectAsync(path, Ct), ErrorCode.JavaInvalidPath);

    [Fact]
    public async Task InspectAsync_DirectoryMissing_ReturnsPathNotFound()
    {
        var missing = Path.Combine(_root, "nope");

        var error = ResultAssert.Failed(await _service.InspectAsync(missing, Ct), ErrorCode.JavaPathNotFound);

        Assert.Equal(missing, error.Details["path"]);
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("noexe", "bin")]
    [InlineData("bin")]
    public async Task InspectAsync_NoExecutable_ReturnsNotAJavaHome(params string[] parts)
    {
        var dir = Directory.CreateDirectory(Path.Combine([_root, .. parts])).FullName;

        ResultAssert.Failed(await _service.InspectAsync(dir, Ct), ErrorCode.JavaNotAJavaHome);
    }

    [Fact]
    public async Task InspectAsync_ExecutableUnrecognisedAndNoRelease_ReturnsNotAJavaHome()
    {
        var home = _java.CreateWithoutRelease("jdk", "not an executable"u8.ToArray());

        ResultAssert.Failed(await _service.InspectAsync(home, Ct), ErrorCode.JavaNotAJavaHome);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task InspectAsync_32Bit_ReturnsNot64Bit(bool fromRelease)
    {
        var home = fromRelease
            ? _java.Create("jre", Release(arch: "i586"))
            : _java.CreateWithoutRelease("jre", PeExecutable(PeI386));

        ResultAssert.Failed(await _service.InspectAsync(home, Ct), ErrorCode.JavaNot64Bit);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task InspectAsync_HomeOrItsBinFolder_ReturnsManualRuntime(bool pickBin, bool trailingSeparator)
    {
        var home = _java.Create("jdk-21", Release("21.0.9", "Eclipse Adoptium"));
        var picked = pickBin ? Path.Combine(home, "bin") : home;

        var runtime = ResultAssert.Ok(await _service.InspectAsync(trailingSeparator ? picked + Path.DirectorySeparatorChar : picked, Ct));

        Assert.Equal(
            new JavaRuntime(home, _java.ExecutableOf(home), new JavaVersion(21, "21.0.9"), "Eclipse Adoptium", JavaRuntimeSource.Manual, HasGraalCompiler: false, HasInstrumentLibrary: true),
            runtime);
    }

    [Fact]
    public async Task InspectAsync_GraalVmHome_ReportsGraalCompiler()
    {
        var home = _java.Create("graalvm-ce-25", Release("25.0.2", "GraalVM Community", graalVersion: "25.0.2"));

        Assert.True(ResultAssert.Ok(await _service.InspectAsync(home, Ct)).HasGraalCompiler);
    }

    [Theory]
    [InlineData(PlatformKind.Windows, true)]
    [InlineData(PlatformKind.Windows, false)]
    [InlineData(PlatformKind.Linux, true)]
    [InlineData(PlatformKind.Linux, false)]
    [InlineData(PlatformKind.MacOS, true)]
    [InlineData(PlatformKind.MacOS, false)]
    public async Task InspectAsync_InstrumentLibraryPresentOrMissing_ReportsIt(PlatformKind os, bool withInstrument)
    {
        var home = new JavaHomeBuilder(_root, os).Create("jdk", withInstrument: withInstrument);
        var service = JavaDiscoveryServiceScanTests.CreateService(new FakePlatformEnvironment { Os = os, PathComparison = StringComparison.Ordinal });

        Assert.Equal(withInstrument, ResultAssert.Ok(await service.InspectAsync(home, Ct)).HasInstrumentLibrary);
    }

    [Fact]
    public async Task InspectAsync_LinuxHomeWithoutRelease_UsesElfHeaderAndUnknownVersion()
    {
        var linux = new JavaHomeBuilder(_root, PlatformKind.Linux);
        var home = linux.CreateWithoutRelease("jdk", ElfExecutable(is64Bit: true));
        var service = JavaDiscoveryServiceScanTests.CreateService(new FakePlatformEnvironment { Os = PlatformKind.Linux, PathComparison = StringComparison.Ordinal });

        var runtime = ResultAssert.Ok(await service.InspectAsync(home, Ct));

        Assert.Equal(new JavaRuntime(home, linux.ExecutableOf(home), null, null, JavaRuntimeSource.Manual, HasGraalCompiler: false, HasInstrumentLibrary: true), runtime);
    }

    [Fact]
    public async Task InspectAsync_ReleaseUnreadable_ReturnsRuntimeWithUnknownVersion()
    {
        var home = _java.CreateWithoutRelease("jdk", PeExecutable(PeAmd64));
        Directory.CreateDirectory(Path.Combine(home, "release"));

        Assert.Null(ResultAssert.Ok(await _service.InspectAsync(home, Ct)).Version);
    }

    [Fact]
    public async Task InspectAsync_CancelledToken_ThrowsOperationCanceled()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _service.InspectAsync(_root, cts.Token));
    }
}
