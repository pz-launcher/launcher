using System.Text.Json;
using System.Text.RegularExpressions;
using PzLauncher.Core.Common;
using PzLauncher.Core.Installs;
using PzLauncher.Core.Java;
using PzLauncher.Core.Launch;
using PzLauncher.Core.Mod.Collections;
using PzLauncher.Core.Mod.ZombieBuddy;
using PzLauncher.Core.Net;
using PzLauncher.Core.Platform;
using PzLauncher.Core.Serialization;
using PzLauncher.Core.Steam;
using PzLauncher.Core.Update;

namespace PzLauncher.Core.Tests.Common;

public partial class ErrorCodeTests
{
    public static TheoryData<ErrorCode> AllCodes => [.. Enum.GetValues<ErrorCode>()];

    private static readonly Dictionary<ErrorCode, (Error Error, ErrorKind Kind)> Factories = new()
    {
        [ErrorCode.ResultUninitialized] = (ResultErrors.Uninitialized, ErrorKind.Unexpected),
        [ErrorCode.SteamNotInstalled] = (SteamErrors.NotInstalled(), ErrorKind.NotFound),
        [ErrorCode.SteamLibraryUnreadable] = (SteamErrors.LibraryUnreadable("p", "r"), ErrorKind.External),
        [ErrorCode.SteamManifestUnreadable] = (SteamErrors.ManifestUnreadable("p", "r"), ErrorKind.External),
        [ErrorCode.SteamVdfMalformed] = (SteamErrors.VdfMalformed("r"), ErrorKind.Validation),
        [ErrorCode.InstallFilesMissing] = (InstallErrors.FilesMissing("p", 1), ErrorKind.NotFound),
        [ErrorCode.InstallInvalidPath] = (InstallErrors.InvalidPath("p"), ErrorKind.Validation),
        [ErrorCode.InstallPathNotFound] = (InstallErrors.PathNotFound("p"), ErrorKind.NotFound),
        [ErrorCode.InstallPathUnreadable] = (InstallErrors.PathUnreadable("p", "r"), ErrorKind.External),
        [ErrorCode.InstallNotAPzInstall] = (InstallErrors.NotAPzInstall("p"), ErrorKind.Validation),
        [ErrorCode.JavaInvalidPath] = (JavaErrors.InvalidPath("p"), ErrorKind.Validation),
        [ErrorCode.JavaPathNotFound] = (JavaErrors.PathNotFound("p"), ErrorKind.NotFound),
        [ErrorCode.JavaNotAJavaHome] = (JavaErrors.NotAJavaHome("p"), ErrorKind.Validation),
        [ErrorCode.JavaNot64Bit] = (JavaErrors.Not64Bit("p"), ErrorKind.Validation),
        [ErrorCode.JavaSourceUnreadable] = (JavaErrors.SourceUnreadable("p", "r"), ErrorKind.External),
        [ErrorCode.JavaReleaseUnreadable] = (JavaErrors.ReleaseUnreadable("p", "r"), ErrorKind.External),
        [ErrorCode.LaunchPlatformUnsupported] = (LaunchErrors.PlatformUnsupported(PlatformKind.MacOS), ErrorKind.Validation),
        [ErrorCode.LaunchInvalidMemory] = (LaunchErrors.InvalidMemory(0), ErrorKind.Validation),
        [ErrorCode.LaunchAlreadyRunning] = (LaunchErrors.AlreadyRunning(), ErrorKind.Conflict),
        [ErrorCode.LaunchNotAGame] = (LaunchErrors.NotAGame("p"), ErrorKind.Validation),
        [ErrorCode.LaunchConfigMissing] = (LaunchErrors.ConfigMissing("p"), ErrorKind.NotFound),
        [ErrorCode.LaunchConfigMalformed] = (LaunchErrors.ConfigMalformed("p", "r"), ErrorKind.External),
        [ErrorCode.LaunchJavaMissing] = (LaunchErrors.JavaMissing("p"), ErrorKind.NotFound),
        [ErrorCode.LaunchStartFailed] = (LaunchErrors.StartFailed("p", "r"), ErrorKind.External),
        [ErrorCode.LaunchGcIncompatible] = (LaunchErrors.GcIncompatible("p"), ErrorKind.Validation),
        [ErrorCode.LaunchGcIncompatibleViewpoint] = (LaunchErrors.GcIncompatibleViewpoint("p"), ErrorKind.Validation),
        [ErrorCode.LaunchGameCrashed] = (LaunchErrors.GameCrashed("p", 1), ErrorKind.External),
        [ErrorCode.LaunchGameExitedEarly] = (LaunchErrors.GameExitedEarly(1, "o"), ErrorKind.External),
        [ErrorCode.LaunchGcConflict] = (LaunchErrors.GcConflict("v"), ErrorKind.Validation),
        [ErrorCode.ZombieBuddyNotSubscribed] = (ZombieBuddyErrors.NotSubscribed("1"), ErrorKind.NotFound),
        [ErrorCode.ZombieBuddySourceFileMissing] = (ZombieBuddyErrors.SourceFileMissing("p"), ErrorKind.NotFound),
        [ErrorCode.ZombieBuddyCopyFailed] = (ZombieBuddyErrors.CopyFailed("p", "r"), ErrorKind.External),
        [ErrorCode.ZombieBuddyDeleteFailed] = (ZombieBuddyErrors.DeleteFailed("p", "r"), ErrorKind.External),
        [ErrorCode.ZombieBuddyConfigWriteFailed] = (ZombieBuddyErrors.ConfigWriteFailed("p", "r"), ErrorKind.External),
        [ErrorCode.ZombieBuddyNotInitialized] = (ZombieBuddyErrors.NotInitialized("p"), ErrorKind.Conflict),
        [ErrorCode.ZombieBuddyModNotPreloadable] = (ZombieBuddyErrors.ModNotPreloadable("m", "p", "r"), ErrorKind.Validation),
        [ErrorCode.ZombieBuddyPreloadConfigInvalid] = (ZombieBuddyErrors.PreloadConfigInvalid("m", "p", "r"), ErrorKind.Validation),
        [ErrorCode.ZombieBuddyPreloadConfigIoFailed] = (ZombieBuddyErrors.PreloadConfigIoFailed("m", "p", "r"), ErrorKind.External),
        [ErrorCode.ModCollectionCacheFolderUnresolved] = (ModCollectionErrors.CacheFolderUnresolved("p"), ErrorKind.Validation),
        [ErrorCode.ModCollectionNameInvalid] = (ModCollectionErrors.NameInvalid("n"), ErrorKind.Validation),
        [ErrorCode.ModCollectionModIdInvalid] = (ModCollectionErrors.ModIdInvalid("m"), ErrorKind.Validation),
        [ErrorCode.ModCollectionNotFound] = (ModCollectionErrors.NotFound("n"), ErrorKind.NotFound),
        [ErrorCode.ModCollectionFileUnreadable] = (ModCollectionErrors.FileUnreadable("p", "r"), ErrorKind.External),
        [ErrorCode.ModCollectionFileMalformed] = (ModCollectionErrors.FileMalformed("p", "r"), ErrorKind.External),
        [ErrorCode.ModCollectionFileWriteFailed] = (ModCollectionErrors.FileWriteFailed("p", "r"), ErrorKind.External),
        [ErrorCode.UpdateNetworkFailed] = (UpdateErrors.NetworkFailed(UpdateSource.GitHub, "r"), ErrorKind.External),
        [ErrorCode.UpdateReleaseInfoInvalid] = (UpdateErrors.ReleaseInfoInvalid(UpdateSource.Gitee, "r"), ErrorKind.External),
        [ErrorCode.UpdatePackageInvalid] = (UpdateErrors.PackageInvalid("p", "r"), ErrorKind.External),
        [ErrorCode.UpdateInstallFolderNotWritable] = (UpdateErrors.InstallFolderNotWritable("p", "r"), ErrorKind.Conflict),
        [ErrorCode.UpdateInstallFailed] = (UpdateErrors.InstallFailed("p", "r"), ErrorKind.External),
        [ErrorCode.UpdateDevelopmentBuild] = (UpdateErrors.DevelopmentBuild(SemVersion.Development), ErrorKind.Conflict),
        [ErrorCode.UpdateRestartFailed] = (UpdateErrors.RestartFailed("p", "r"), ErrorKind.External),
        [ErrorCode.NetworkProxyHostInvalid] = (NetworkErrors.ProxyHostInvalid("h"), ErrorKind.Validation),
        [ErrorCode.NetworkProxyPortInvalid] = (NetworkErrors.ProxyPortInvalid(0), ErrorKind.Validation),
        [ErrorCode.NetworkUnreachable] = (NetworkErrors.Unreachable("t", "r"), ErrorKind.External),
        [ErrorCode.NetworkTimeout] = (NetworkErrors.Timeout("t", TimeSpan.FromSeconds(15)), ErrorKind.External),
    };

    [GeneratedRegex(@"^[a-z]+\.[a-z0-9]+(_[a-z0-9]+)*$")]
    private static partial Regex WireNameFormat();

    [Theory]
    [MemberData(nameof(AllCodes))]
    public void ToWireName_EveryCode_MatchesFeatureDotSnakeCase(ErrorCode code) =>
        Assert.Matches(WireNameFormat(), code.ToWireName());

    [Fact]
    public void ToWireName_AllCodes_AreUnique()
    {
        var names = Enum.GetValues<ErrorCode>().Select(c => c.ToWireName()).ToList();

        Assert.Equal(names.Count, names.Distinct(StringComparer.Ordinal).Count());
    }

    [Theory]
    [MemberData(nameof(AllCodes))]
    public void Json_EveryCode_RoundTripsThroughWireName(ErrorCode code)
    {
        var json = JsonSerializer.Serialize(code, CoreJsonContext.Default.ErrorCode);

        Assert.Equal($"\"{code.ToWireName()}\"", json);
        Assert.Equal(code, JsonSerializer.Deserialize(json, CoreJsonContext.Default.ErrorCode));
    }

    // Wire names are a public contract: this pins the existing ones so a rename shows up in review.
    [Theory]
    [InlineData(ErrorCode.ResultUninitialized, "result.uninitialized")]
    [InlineData(ErrorCode.SteamNotInstalled, "steam.not_installed")]
    [InlineData(ErrorCode.SteamLibraryUnreadable, "steam.library_unreadable")]
    [InlineData(ErrorCode.SteamManifestUnreadable, "steam.manifest_unreadable")]
    [InlineData(ErrorCode.SteamVdfMalformed, "steam.vdf_malformed")]
    [InlineData(ErrorCode.InstallFilesMissing, "install.files_missing")]
    [InlineData(ErrorCode.InstallInvalidPath, "install.invalid_path")]
    [InlineData(ErrorCode.InstallPathNotFound, "install.path_not_found")]
    [InlineData(ErrorCode.InstallPathUnreadable, "install.path_unreadable")]
    [InlineData(ErrorCode.InstallNotAPzInstall, "install.not_a_pz_install")]
    [InlineData(ErrorCode.JavaInvalidPath, "java.invalid_path")]
    [InlineData(ErrorCode.JavaPathNotFound, "java.path_not_found")]
    [InlineData(ErrorCode.JavaNotAJavaHome, "java.not_a_java_home")]
    [InlineData(ErrorCode.JavaNot64Bit, "java.not_64_bit")]
    [InlineData(ErrorCode.JavaSourceUnreadable, "java.source_unreadable")]
    [InlineData(ErrorCode.JavaReleaseUnreadable, "java.release_unreadable")]
    [InlineData(ErrorCode.LaunchPlatformUnsupported, "launch.platform_unsupported")]
    [InlineData(ErrorCode.LaunchInvalidMemory, "launch.invalid_memory")]
    [InlineData(ErrorCode.LaunchAlreadyRunning, "launch.already_running")]
    [InlineData(ErrorCode.LaunchNotAGame, "launch.not_a_game")]
    [InlineData(ErrorCode.LaunchConfigMissing, "launch.config_missing")]
    [InlineData(ErrorCode.LaunchConfigMalformed, "launch.config_malformed")]
    [InlineData(ErrorCode.LaunchJavaMissing, "launch.java_missing")]
    [InlineData(ErrorCode.LaunchStartFailed, "launch.start_failed")]
    [InlineData(ErrorCode.LaunchGcIncompatible, "launch.gc_incompatible")]
    [InlineData(ErrorCode.LaunchGcIncompatibleViewpoint, "launch.gc_incompatible_viewpoint")]
    [InlineData(ErrorCode.LaunchGameCrashed, "launch.game_crashed")]
    [InlineData(ErrorCode.LaunchGameExitedEarly, "launch.game_exited_early")]
    [InlineData(ErrorCode.LaunchGcConflict, "launch.gc_conflict")]
    [InlineData(ErrorCode.ZombieBuddyNotSubscribed, "zombiebuddy.not_subscribed")]
    [InlineData(ErrorCode.ZombieBuddySourceFileMissing, "zombiebuddy.source_file_missing")]
    [InlineData(ErrorCode.ZombieBuddyCopyFailed, "zombiebuddy.copy_failed")]
    [InlineData(ErrorCode.ZombieBuddyDeleteFailed, "zombiebuddy.delete_failed")]
    [InlineData(ErrorCode.ZombieBuddyConfigWriteFailed, "zombiebuddy.config_write_failed")]
    [InlineData(ErrorCode.ZombieBuddyNotInitialized, "zombiebuddy.not_initialized")]
    [InlineData(ErrorCode.ZombieBuddyModNotPreloadable, "zombiebuddy.mod_not_preloadable")]
    [InlineData(ErrorCode.ZombieBuddyPreloadConfigInvalid, "zombiebuddy.preload_config_invalid")]
    [InlineData(ErrorCode.ZombieBuddyPreloadConfigIoFailed, "zombiebuddy.preload_config_io_failed")]
    [InlineData(ErrorCode.ModCollectionCacheFolderUnresolved, "modcollection.cache_folder_unresolved")]
    [InlineData(ErrorCode.ModCollectionNameInvalid, "modcollection.name_invalid")]
    [InlineData(ErrorCode.ModCollectionModIdInvalid, "modcollection.mod_id_invalid")]
    [InlineData(ErrorCode.ModCollectionNotFound, "modcollection.not_found")]
    [InlineData(ErrorCode.ModCollectionFileUnreadable, "modcollection.file_unreadable")]
    [InlineData(ErrorCode.ModCollectionFileMalformed, "modcollection.file_malformed")]
    [InlineData(ErrorCode.ModCollectionFileWriteFailed, "modcollection.file_write_failed")]
    [InlineData(ErrorCode.UpdateNetworkFailed, "update.network_failed")]
    [InlineData(ErrorCode.UpdateReleaseInfoInvalid, "update.release_info_invalid")]
    [InlineData(ErrorCode.UpdatePackageInvalid, "update.package_invalid")]
    [InlineData(ErrorCode.UpdateInstallFolderNotWritable, "update.install_folder_not_writable")]
    [InlineData(ErrorCode.UpdateInstallFailed, "update.install_failed")]
    [InlineData(ErrorCode.UpdateDevelopmentBuild, "update.development_build")]
    [InlineData(ErrorCode.UpdateRestartFailed, "update.restart_failed")]
    [InlineData(ErrorCode.NetworkProxyHostInvalid, "network.proxy_host_invalid")]
    [InlineData(ErrorCode.NetworkProxyPortInvalid, "network.proxy_port_invalid")]
    [InlineData(ErrorCode.NetworkUnreachable, "network.unreachable")]
    [InlineData(ErrorCode.NetworkTimeout, "network.timeout")]
    public void ToWireName_ExistingCodes_AreStable(ErrorCode code, string wireName) =>
        Assert.Equal(wireName, code.ToWireName());

    [Theory]
    [MemberData(nameof(AllCodes))]
    public void Factory_EveryCode_BuildsThatCodeWithItsKind(ErrorCode code)
    {
        Assert.True(Factories.TryGetValue(code, out var entry), $"Add {code} to {nameof(Factories)}.");
        Assert.Equal(code, entry.Error.Code);
        Assert.Equal(entry.Kind, entry.Error.Kind);
    }
}
