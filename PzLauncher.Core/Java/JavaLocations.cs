using PzLauncher.Core.Platform;

namespace PzLauncher.Core.Java;

/// <param name="Suffix">Sub-path below each enumerated subkey, e.g. <c>hotspot\MSI</c>.</param>
internal sealed record JavaRegistryLocation(string Key, string? Suffix, string ValueName);

// Registry layouts per vendor: task research/registry-layouts.md.
internal static class JavaLocations
{
    public const string BinFolder = "bin";

    public const string ReleaseFile = "release";

    // A JDK 8 home has a full JRE in <home>/jre.
    public const string LegacyJreFolder = "jre";

    public const string SdkmanDirVariable = "SDKMAN_DIR";

    public static string ExecutableName(PlatformKind os) => os switch
    {
        PlatformKind.Windows => "javaw.exe",
        PlatformKind.MacOS or PlatformKind.Linux => "java",
    };

    public static char PathListSeparator(PlatformKind os) => os switch
    {
        PlatformKind.Windows => ';',
        PlatformKind.MacOS or PlatformKind.Linux => ':',
    };

    public static IReadOnlyList<RegistryHiveKind> RegistryHives { get; } =
        [RegistryHiveKind.LocalMachine, RegistryHiveKind.CurrentUser];

    public static IReadOnlyList<JavaRegistryLocation> Registry { get; } =
    [
        new(@"SOFTWARE\JavaSoft\JDK", null, "JavaHome"),
        new(@"SOFTWARE\JavaSoft\JRE", null, "JavaHome"),
        new(@"SOFTWARE\JavaSoft\Java Development Kit", null, "JavaHome"),
        new(@"SOFTWARE\JavaSoft\Java Runtime Environment", null, "JavaHome"),
        new(@"SOFTWARE\Eclipse Adoptium\JDK", @"hotspot\MSI", "Path"),
        new(@"SOFTWARE\Eclipse Adoptium\JRE", @"hotspot\MSI", "Path"),
        new(@"SOFTWARE\Eclipse Foundation\JDK", @"hotspot\MSI", "Path"),
        new(@"SOFTWARE\AdoptOpenJDK\JDK", @"hotspot\MSI", "Path"),
        new(@"SOFTWARE\AdoptOpenJDK\JDK", @"openj9\MSI", "Path"),
        new(@"SOFTWARE\AdoptOpenJDK\JRE", @"hotspot\MSI", "Path"),
        new(@"SOFTWARE\AdoptOpenJDK\JRE", @"openj9\MSI", "Path"),
        new(@"SOFTWARE\Semeru\JDK", @"openj9\MSI", "Path"),
        new(@"SOFTWARE\Semeru\JRE", @"openj9\MSI", "Path"),
        new(@"SOFTWARE\Microsoft\JDK", @"hotspot\MSI", "Path"),
        new(@"SOFTWARE\Azul Systems\Zulu", null, "InstallationPath"),
        new(@"SOFTWARE\BellSoft\Liberica", null, "InstallationPath"),
    ];

    /// <summary>Folders under <c>%ProgramFiles%</c> whose sub-folders are Java homes.</summary>
    public static IReadOnlyList<string> WindowsProgramFilesFolders { get; } =
        ["Java", "Eclipse Adoptium", "Zulu", "Microsoft", "BellSoft", "Amazon Corretto", "Semeru"];

    /// <summary>Folders whose sub-folders are Java homes.</summary>
    public static IReadOnlyList<string> LinuxSystemFolders { get; } =
        ["/usr/lib/jvm", "/usr/lib64/jvm", "/usr/java", "/opt"];

    /// <summary>IntelliJ's download folder, relative to the home directory, on every OS.</summary>
    public static IReadOnlyList<string> HomeFolders { get; } = [".jdks"];

    /// <summary>Used when <see cref="SdkmanDirVariable"/> is unset; relative to the home directory.</summary>
    public const string DefaultSdkmanDir = ".sdkman";

    public static string SdkmanCandidates(string sdkmanDir) => Path.Combine(sdkmanDir, "candidates", "java");
}
