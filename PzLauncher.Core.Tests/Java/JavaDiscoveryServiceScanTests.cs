using Microsoft.Extensions.Logging.Abstractions;
using PzLauncher.Core.Common;
using PzLauncher.Core.Java;
using PzLauncher.Core.Platform;
using PzLauncher.Core.Tests.TestSupport;
using static PzLauncher.Core.Tests.TestSupport.JavaHomeBuilder;

namespace PzLauncher.Core.Tests.Java;

public sealed class JavaDiscoveryServiceScanTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly string _root;
    private readonly JavaHomeBuilder _windows;
    private readonly JavaHomeBuilder _linux;

    public JavaDiscoveryServiceScanTests()
    {
        _root = JavaDiscoveryService.ResolveRealPath(_temp.Path);
        _windows = new JavaHomeBuilder(_root, PlatformKind.Windows);
        _linux = new JavaHomeBuilder(_root, PlatformKind.Linux);
    }

    public void Dispose() => _temp.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<int, RegistryHiveKind> RegistryRows
    {
        get
        {
            var data = new TheoryData<int, RegistryHiveKind>();
            for (var row = 0; row < JavaLocations.Registry.Count; row++)
            {
                data.Add(row, RegistryHiveKind.LocalMachine);
                data.Add(row, RegistryHiveKind.CurrentUser);
            }

            return data;
        }
    }

    public static TheoryData<PlatformKind, string?, byte[], bool> Bitness => new()
    {
        { PlatformKind.Windows, Release(arch: "x86_64"), [], true },
        { PlatformKind.Windows, Release(arch: "amd64"), [], true },
        { PlatformKind.Windows, Release(arch: "x86"), [], false },
        { PlatformKind.Windows, Release(arch: "i586"), [], false },
        { PlatformKind.Windows, Release(arch: "x86_64"), PeExecutable(PeI386), true },
        { PlatformKind.Windows, Release(arch: null), PeExecutable(PeAmd64), true },
        { PlatformKind.Windows, Release(arch: null), PeExecutable(PeI386), false },
        { PlatformKind.Windows, null, PeExecutable(PeAmd64), true },
        { PlatformKind.Windows, null, PeExecutable(PeArm64), true },
        { PlatformKind.Windows, null, PeExecutable(PeI386), false },
        { PlatformKind.Windows, null, [], false },
        { PlatformKind.Linux, Release(arch: "aarch64"), [], true },
        { PlatformKind.Linux, null, ElfExecutable(is64Bit: true), true },
        { PlatformKind.Linux, null, ElfExecutable(is64Bit: false), false },
        { PlatformKind.Linux, null, [], false },
    };

    private string Dir(params string[] parts) => Path.Combine([_root, .. parts]);

    private FakePlatformEnvironment Windows() => new()
    {
        Os = PlatformKind.Windows,
        HomeDirectory = Dir("home"),
        PathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal,
    };

    private FakePlatformEnvironment Linux() => new()
    {
        Os = PlatformKind.Linux,
        HomeDirectory = Dir("home"),
        PathComparison = StringComparison.Ordinal,
    };

    private FakePlatformEnvironment For(PlatformKind os) => os == PlatformKind.Windows ? Windows() : Linux();

    internal static JavaDiscoveryService CreateService(FakePlatformEnvironment env, params string[] linuxSystemFolders) =>
        new(env, NullLogger<JavaDiscoveryService>.Instance) { LinuxSystemFolders = linuxSystemFolders };

    private static async Task<JavaScanResult> ScanAsync(FakePlatformEnvironment env, params string[] linuxSystemFolders) =>
        ResultAssert.Ok(await CreateService(env, linuxSystemFolders).ScanAsync(Ct));

    private static void AssertFound(JavaScanResult scan, params (string Home, JavaRuntimeSource Source)[] expected)
    {
        Assert.Empty(scan.Warnings);
        Assert.Equal(expected, scan.Runtimes.Select(r => (r.HomePath, r.Source)));
    }

    [Fact]
    public async Task ScanAsync_WindowsSources_FindsEachWithItsSourceOrderedByVersion()
    {
        var fromJavaHome = _windows.Create("jh", Release("25.0.1"));
        var fromPath = _windows.Create("onpath", Release("24"));
        var fromHklm = _windows.Create("hklm", Release("23"));
        var fromHkcu = _windows.Create("hkcu", Release("22"));
        var fromProgramFiles = _windows.Create(Path.Combine("PF", "Eclipse Adoptium", "jdk-21.0.9.10-hotspot"), Release("21.0.9"));
        var fromJdks = _windows.Create(Path.Combine("home", ".jdks", "temurin-17"), Release("17"));
        var env = Windows();
        env.EnvironmentVariables["JAVA_HOME"] = fromJavaHome;
        env.EnvironmentVariables["PATH"] = $"{Dir("nojava")};{Path.Combine(fromPath, "bin")}";
        env.EnvironmentVariables["ProgramFiles"] = Dir("PF");
        env.SetRegistryValue(RegistryHiveKind.LocalMachine, @"SOFTWARE\Eclipse Adoptium\JDK\23.0.0.1\hotspot\MSI", "Path", fromHklm + Path.DirectorySeparatorChar);
        env.SetRegistryValue(RegistryHiveKind.CurrentUser, @"SOFTWARE\Azul Systems\Zulu\zulu-22", "InstallationPath", fromHkcu);

        var scan = await ScanAsync(env);

        AssertFound(
            scan,
            (fromJavaHome, JavaRuntimeSource.JavaHome),
            (fromPath, JavaRuntimeSource.Path),
            (fromHklm, JavaRuntimeSource.Registry),
            (fromHkcu, JavaRuntimeSource.Registry),
            (fromProgramFiles, JavaRuntimeSource.KnownFolder),
            (fromJdks, JavaRuntimeSource.KnownFolder));
        Assert.Equal(
            new JavaRuntime(fromProgramFiles, _windows.ExecutableOf(fromProgramFiles), new JavaVersion(21, "21.0.9"), "Eclipse Adoptium", JavaRuntimeSource.KnownFolder),
            scan.Runtimes[4]);
    }

    [Theory]
    [MemberData(nameof(RegistryRows))]
    public async Task ScanAsync_EveryRegistryLocation_FindsTheHomeInEitherHive(int row, RegistryHiveKind hive)
    {
        var location = JavaLocations.Registry[row];
        var home = _windows.Create("jdk");
        var keyPath = location.Suffix is null ? $@"{location.Key}\17.0.9" : $@"{location.Key}\17.0.9\{location.Suffix}";
        var env = Windows();
        env.SetRegistryValue(hive, keyPath, location.ValueName, home);

        AssertFound(await ScanAsync(env), (home, JavaRuntimeSource.Registry));
    }

    [Fact]
    public async Task ScanAsync_LinuxSources_FindsEachWithItsSource()
    {
        var fromJavaHome = _linux.Create("jh", Release("25"));
        var fromJvm = _linux.Create(Path.Combine("usr", "lib", "jvm", "java-21-openjdk-amd64"), Release("21.0.5"));
        var fromOpt = _linux.CreateWithoutRelease(Path.Combine("opt", "jdk-17"), ElfExecutable(is64Bit: true));
        var fromJdks = _linux.Create(Path.Combine("home", ".jdks", "temurin-11"), Release("11.0.2"));
        var fromSdkman = _linux.Create(Path.Combine("home", ".sdkman", "candidates", "java", "8.0.392-tem"), Release("1.8.0_392"));
        var env = Linux();
        env.EnvironmentVariables["JAVA_HOME"] = fromJavaHome;
        env.SetRegistryValue(RegistryHiveKind.LocalMachine, @"SOFTWARE\JavaSoft\JDK\21", "JavaHome", fromJvm);

        var scan = await ScanAsync(env, Dir("usr", "lib", "jvm"), Dir("opt"));

        AssertFound(
            scan,
            (fromJavaHome, JavaRuntimeSource.JavaHome),
            (fromJvm, JavaRuntimeSource.KnownFolder),
            (fromJdks, JavaRuntimeSource.KnownFolder),
            (fromSdkman, JavaRuntimeSource.KnownFolder),
            (fromOpt, JavaRuntimeSource.KnownFolder));
        Assert.Equal(new JavaVersion(8, "1.8.0_392"), scan.Runtimes[3].Version);
        Assert.Null(scan.Runtimes[4].Version);
    }

    [Fact]
    public async Task ScanAsync_SdkmanDirSet_ScansItInsteadOfTheDefault()
    {
        var custom = _linux.Create(Path.Combine("sdk", "candidates", "java", "21-tem"));
        _linux.Create(Path.Combine("home", ".sdkman", "candidates", "java", "17-tem"));
        var env = Linux();
        env.EnvironmentVariables["SDKMAN_DIR"] = Dir("sdk");

        AssertFound(await ScanAsync(env), (custom, JavaRuntimeSource.KnownFolder));
    }

    [Fact]
    public async Task ScanAsync_LinuxPathExecutableIsASymlinkChain_ReportsTheRealHomeOnce()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Linux PATH uses ':', which a Windows temp path also contains.");
        var home = _linux.Create(Path.Combine("usr", "lib", "jvm", "java-17-openjdk"), Release("17"));
        var alternative = Dir("etc", "alternatives", "java");
        Links.File(alternative, _linux.ExecutableOf(home));
        Links.File(Dir("usr", "bin", "java"), alternative);
        var env = Linux();
        env.EnvironmentVariables["PATH"] = $"{Dir("nojava")}:{Dir("usr", "bin")}";

        AssertFound(await ScanAsync(env, Dir("usr", "lib", "jvm")), (home, JavaRuntimeSource.Path));
    }

    [Fact]
    public async Task ScanAsync_PathEntryIsALinkedShim_ReportsTheRealHomeOnce()
    {
        var home = _windows.Create(Path.Combine("PF", "Java", "jdk-21"));
        Links.File(Dir("javapath", _windows.ExecutableName), _windows.ExecutableOf(home));
        var env = Windows();
        env.EnvironmentVariables["PATH"] = Dir("javapath");
        env.EnvironmentVariables["ProgramFiles"] = Dir("PF");

        AssertFound(await ScanAsync(env), (home, JavaRuntimeSource.Path));
    }

    [Fact]
    public async Task ScanAsync_HomeReachedThroughDirectoryLinks_ReportedOnce()
    {
        var home = _linux.Create(Path.Combine("usr", "lib", "jvm", "java-21-openjdk"), Release("21"));
        Links.Directory(Dir("usr", "lib", "jvm", "default-java"), home);
        Links.Directory(Dir("usr", "lib64"), Dir("usr", "lib"));
        Links.Directory(Dir("home", ".sdkman", "candidates", "java", "current"), home);

        var scan = await ScanAsync(Linux(), Dir("usr", "lib64", "jvm"), Dir("usr", "lib", "jvm"));

        AssertFound(scan, (home, JavaRuntimeSource.KnownFolder));
    }

    [Fact]
    public async Task ScanAsync_SameHomeFromEverySource_ReportedOnceWithTheFirstSource()
    {
        var home = _windows.Create(Path.Combine("PF", "Java", "jdk-17"), Release("17.0.9"));
        var env = Windows();
        env.EnvironmentVariables["JAVA_HOME"] = home + Path.DirectorySeparatorChar;
        env.EnvironmentVariables["PATH"] = Path.Combine(home, "bin");
        env.EnvironmentVariables["ProgramFiles"] = Dir("PF");
        env.SetRegistryValue(RegistryHiveKind.LocalMachine, @"SOFTWARE\JavaSoft\JDK\17", "JavaHome", home);
        env.SetRegistryValue(RegistryHiveKind.LocalMachine, @"SOFTWARE\JavaSoft\JDK\17.0.9", "JavaHome", home);

        AssertFound(await ScanAsync(env), (home, JavaRuntimeSource.JavaHome));
    }

    [Fact]
    public async Task ScanAsync_Jdk8JreFolder_ReportedAsItsJdk()
    {
        var jdk = _windows.Create("jdk1.8.0_392", Release("1.8.0_392"));
        _windows.Create(Path.Combine("jdk1.8.0_392", "jre"), Release("1.8.0_392"));
        var env = Windows();
        env.SetRegistryValue(RegistryHiveKind.LocalMachine, @"SOFTWARE\JavaSoft\Java Runtime Environment\1.8", "JavaHome", Path.Combine(jdk, "jre"));
        env.SetRegistryValue(RegistryHiveKind.LocalMachine, @"SOFTWARE\JavaSoft\Java Development Kit\1.8", "JavaHome", jdk);

        AssertFound(await ScanAsync(env), (jdk, JavaRuntimeSource.Registry));
    }

    [Fact]
    public async Task ScanAsync_FoldersWithoutTheExecutable_SkippedSilently()
    {
        Directory.CreateDirectory(Dir("PF", "Java", "broken", "bin"));
        Directory.CreateDirectory(Dir("PF", "Java", "notes"));
        _linux.Create(Path.Combine("PF", "Java", "linux-only"));
        var env = Windows();
        env.EnvironmentVariables["ProgramFiles"] = Dir("PF");
        env.EnvironmentVariables["JAVA_HOME"] = Dir("PF", "Java", "broken", "bin");

        AssertFound(await ScanAsync(env));
    }

    [Fact]
    public async Task ScanAsync_InvalidOrRelativeValues_SkippedSilently()
    {
        var env = Windows();
        env.EnvironmentVariables["JAVA_HOME"] = "relative";
        env.EnvironmentVariables["PATH"] = ";;bin;bad\0path; ";
        env.EnvironmentVariables["ProgramFiles"] = "Program Files";
        env.SetRegistryValue(RegistryHiveKind.LocalMachine, @"SOFTWARE\JavaSoft\JDK\17", "JavaHome", "bad\0path");

        AssertFound(await ScanAsync(env));
    }

    [Theory]
    [MemberData(nameof(Bitness))]
    public async Task ScanAsync_Bitness_ListsOnly64Bit(PlatformKind os, string? release, byte[] executable, bool listed)
    {
        var builder = os == PlatformKind.Windows ? _windows : _linux;
        var home = release is null ? builder.CreateWithoutRelease("jdk", executable) : builder.Create("jdk", release, executable);
        var env = For(os);
        env.EnvironmentVariables["JAVA_HOME"] = home;

        var scan = await ScanAsync(env);

        AssertFound(scan, listed ? [(home, JavaRuntimeSource.JavaHome)] : []);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("garbage\nIMPLEMENTOR\n=\"x\"\n")]
    public async Task ScanAsync_ReleaseMissingOrMalformed_ListedWithUnknownVersionAndVendorWithoutWarning(string? release)
    {
        var home = release is null
            ? _windows.CreateWithoutRelease("jdk", PeExecutable(PeAmd64))
            : _windows.Create("jdk", release, PeExecutable(PeAmd64));
        var env = Windows();
        env.EnvironmentVariables["JAVA_HOME"] = home;

        var scan = await ScanAsync(env);

        Assert.Empty(scan.Warnings);
        Assert.Equal(new JavaRuntime(home, _windows.ExecutableOf(home), null, null, JavaRuntimeSource.JavaHome), Assert.Single(scan.Runtimes));
    }

    [Fact]
    public async Task ScanAsync_ReleaseUnreadable_WarnsAndStillListsFromTheHeader()
    {
        var home = _windows.CreateWithoutRelease("jdk", PeExecutable(PeAmd64));
        var release = Path.Combine(home, "release");
        Directory.CreateDirectory(release);
        var env = Windows();
        env.EnvironmentVariables["JAVA_HOME"] = home;

        var scan = await ScanAsync(env);

        Assert.Null(Assert.Single(scan.Runtimes).Version);
        Assert.Equal(release, ResultAssert.SingleWarning(scan, ErrorCode.JavaReleaseUnreadable).Details["path"]);
    }

    [Theory]
    [InlineData(@"SOFTWARE\Eclipse Adoptium\JDK")]
    [InlineData(@"SOFTWARE\Eclipse Adoptium\JDK\21.0.9.10\hotspot\MSI")]
    public async Task ScanAsync_RegistryKeyUnreadable_WarnsAndScansTheRest(string unreadableKey)
    {
        var javaHome = _windows.Create("jh");
        var fromRegistry = _windows.Create("zulu");
        var env = Windows();
        env.EnvironmentVariables["JAVA_HOME"] = javaHome;
        env.SetRegistryValue(RegistryHiveKind.LocalMachine, @"SOFTWARE\Eclipse Adoptium\JDK\21.0.9.10\hotspot\MSI", "Path", Dir("adoptium"));
        env.SetRegistryValue(RegistryHiveKind.LocalMachine, @"SOFTWARE\Azul Systems\Zulu\zulu-21", "InstallationPath", fromRegistry);
        env.UnreadableRegistryKeys.Add((RegistryHiveKind.LocalMachine, unreadableKey));

        var scan = await ScanAsync(env);

        Assert.Equal([javaHome, fromRegistry], scan.Runtimes.Select(r => r.HomePath));
        var warning = ResultAssert.SingleWarning(scan, ErrorCode.JavaSourceUnreadable);
        Assert.Equal($@"HKLM\{unreadableKey}", warning.Details["path"]);
    }

    [Fact]
    public async Task ScanAsync_KnownFolderUnreadable_WarnsAndScansTheRest()
    {
        var javaHome = _windows.Create("jh");
        _windows.Create(Path.Combine("PF", "Java", "jdk-21"));
        var fromJdks = _windows.Create(Path.Combine("home", ".jdks", "jdk-17"), Release("17"));
        var env = Windows();
        env.EnvironmentVariables["JAVA_HOME"] = javaHome;
        env.EnvironmentVariables["ProgramFiles"] = Dir("PF");

        var result = await DeniedListing.RunAsync(Dir("PF", "Java"), () => CreateService(env).ScanAsync(Ct));

        var scan = ResultAssert.Ok(result);
        Assert.Equal([javaHome, fromJdks], scan.Runtimes.Select(r => r.HomePath));
        Assert.Equal(Dir("PF", "Java"), ResultAssert.SingleWarning(scan, ErrorCode.JavaSourceUnreadable).Details["path"]);
    }

    [Fact]
    public async Task ScanAsync_MacOS_FindsNothing()
    {
        var env = new FakePlatformEnvironment { Os = PlatformKind.MacOS, HomeDirectory = Dir("home"), PathComparison = StringComparison.Ordinal };
        env.EnvironmentVariables["JAVA_HOME"] = _linux.Create("jdk");

        AssertFound(await ScanAsync(env));
    }

    [Fact]
    public async Task ScanAsync_CancelledToken_ThrowsOperationCanceled()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateService(Windows()).ScanAsync(cts.Token));
    }
}
