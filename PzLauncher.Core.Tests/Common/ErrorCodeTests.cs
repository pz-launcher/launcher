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
using PzLauncher.Core.Steam.Workshop;
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
        [ErrorCode.SteamPlatformUnsupported] = (SteamErrors.PlatformUnsupported(PlatformKind.Linux), ErrorKind.Validation),
        [ErrorCode.SteamNoCurrentAccount] = (SteamErrors.NoCurrentAccount("p", "r"), ErrorKind.NotFound),
        [ErrorCode.SteamAppNotInLocalConfig] = (SteamErrors.AppNotInLocalConfig("p", 1), ErrorKind.NotFound),
        [ErrorCode.SteamLocalConfigMalformed] = (SteamErrors.LocalConfigMalformed("p", "r"), ErrorKind.External),
        [ErrorCode.SteamLocalConfigUnwritable] = (SteamErrors.LocalConfigUnwritable("p", "r"), ErrorKind.External),
        [ErrorCode.SteamClientRunning] = (SteamErrors.ClientRunning(), ErrorKind.Conflict),
        [ErrorCode.SteamShutdownTimedOut] = (SteamErrors.ShutdownTimedOut(TimeSpan.FromSeconds(60)), ErrorKind.External),
        [ErrorCode.SteamShutdownUnconfirmed] = (SteamErrors.ShutdownUnconfirmed("r"), ErrorKind.External),
        [ErrorCode.SteamStartFailed] = (SteamErrors.StartFailed("p", "r"), ErrorKind.External),
        [ErrorCode.WorkshopSubscriptionsUnreadable] = (WorkshopErrors.SubscriptionsUnreadable("p", "r"), ErrorKind.External),
        [ErrorCode.WorkshopManifestUnreadable] = (WorkshopErrors.ManifestUnreadable("p", "r"), ErrorKind.External),
        [ErrorCode.WorkshopNetworkFailed] = (WorkshopErrors.NetworkFailed("r"), ErrorKind.External),
        [ErrorCode.WorkshopResponseInvalid] = (WorkshopErrors.ResponseInvalid("r"), ErrorKind.External),
        [ErrorCode.WorkshopCollectionNotFound] = (WorkshopErrors.CollectionNotFound(5), ErrorKind.NotFound),
        [ErrorCode.WorkshopSteamApiMissing] = (WorkshopErrors.SteamApiMissing("p"), ErrorKind.NotFound),
        [ErrorCode.WorkshopSteamApiUnusable] = (WorkshopErrors.SteamApiUnusable("p", "r"), ErrorKind.External),
        [ErrorCode.WorkshopSteamNotRunning] = (WorkshopErrors.SteamNotRunning(), ErrorKind.Conflict),
        [ErrorCode.WorkshopSteamInitFailed] = (WorkshopErrors.SteamInitFailed("r"), ErrorKind.External),
        [ErrorCode.WorkshopHelperStartFailed] = (WorkshopErrors.HelperStartFailed("p", "r"), ErrorKind.External),
        [ErrorCode.WorkshopHelperFailed] = (WorkshopErrors.HelperFailed("r"), ErrorKind.External),
        [ErrorCode.WorkshopHelperRequestRejected] = (WorkshopErrors.HelperRequestRejected("r"), ErrorKind.Unexpected),
        [ErrorCode.WorkshopHelperTimedOut] = (WorkshopErrors.HelperTimedOut(TimeSpan.FromMinutes(3)), ErrorKind.External),
        [ErrorCode.WorkshopCollectionsQueryFailed] = (WorkshopErrors.CollectionsQueryFailed("r"), ErrorKind.External),
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
        [ErrorCode.LaunchConfigWriteFailed] = (LaunchErrors.ConfigWriteFailed("p", "r"), ErrorKind.External),
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
    [InlineData(ErrorCode.SteamPlatformUnsupported, "steam.platform_unsupported")]
    [InlineData(ErrorCode.SteamNoCurrentAccount, "steam.no_current_account")]
    [InlineData(ErrorCode.SteamAppNotInLocalConfig, "steam.app_not_in_local_config")]
    [InlineData(ErrorCode.SteamLocalConfigMalformed, "steam.local_config_malformed")]
    [InlineData(ErrorCode.SteamLocalConfigUnwritable, "steam.local_config_unwritable")]
    [InlineData(ErrorCode.SteamClientRunning, "steam.client_running")]
    [InlineData(ErrorCode.SteamShutdownTimedOut, "steam.shutdown_timed_out")]
    [InlineData(ErrorCode.SteamShutdownUnconfirmed, "steam.shutdown_unconfirmed")]
    [InlineData(ErrorCode.SteamStartFailed, "steam.start_failed")]
    [InlineData(ErrorCode.WorkshopSubscriptionsUnreadable, "workshop.subscriptions_unreadable")]
    [InlineData(ErrorCode.WorkshopManifestUnreadable, "workshop.manifest_unreadable")]
    [InlineData(ErrorCode.WorkshopNetworkFailed, "workshop.network_failed")]
    [InlineData(ErrorCode.WorkshopResponseInvalid, "workshop.response_invalid")]
    [InlineData(ErrorCode.WorkshopCollectionNotFound, "workshop.collection_not_found")]
    [InlineData(ErrorCode.WorkshopSteamApiMissing, "workshop.steam_api_missing")]
    [InlineData(ErrorCode.WorkshopSteamApiUnusable, "workshop.steam_api_unusable")]
    [InlineData(ErrorCode.WorkshopSteamNotRunning, "workshop.steam_not_running")]
    [InlineData(ErrorCode.WorkshopSteamInitFailed, "workshop.steam_init_failed")]
    [InlineData(ErrorCode.WorkshopHelperStartFailed, "workshop.helper_start_failed")]
    [InlineData(ErrorCode.WorkshopHelperFailed, "workshop.helper_failed")]
    [InlineData(ErrorCode.WorkshopHelperRequestRejected, "workshop.helper_request_rejected")]
    [InlineData(ErrorCode.WorkshopHelperTimedOut, "workshop.helper_timed_out")]
    [InlineData(ErrorCode.WorkshopCollectionsQueryFailed, "workshop.collections_query_failed")]
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
    [InlineData(ErrorCode.LaunchConfigWriteFailed, "launch.config_write_failed")]
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

    [Theory]
    [InlineData(ErrorCode.InstallInvalidPath)]
    [InlineData(ErrorCode.InstallPathNotFound)]
    [InlineData(ErrorCode.InstallNotAPzInstall)]
    [InlineData(ErrorCode.JavaInvalidPath)]
    [InlineData(ErrorCode.JavaPathNotFound)]
    [InlineData(ErrorCode.JavaNotAJavaHome)]
    [InlineData(ErrorCode.JavaNot64Bit)]
    [InlineData(ErrorCode.LaunchNotAGame)]
    [InlineData(ErrorCode.LaunchConfigMissing)]
    [InlineData(ErrorCode.LaunchJavaMissing)]
    [InlineData(ErrorCode.LaunchGcIncompatible)]
    [InlineData(ErrorCode.LaunchGcIncompatibleViewpoint)]
    [InlineData(ErrorCode.ModCollectionCacheFolderUnresolved)]
    [InlineData(ErrorCode.ZombieBuddySourceFileMissing)]
    [InlineData(ErrorCode.ZombieBuddyNotInitialized)]
    [InlineData(ErrorCode.WorkshopSteamApiMissing)]
    public void Factory_PathDetails_HoldOnlyThePath(ErrorCode code) =>
        Assert.Equal(new Dictionary<string, string> { ["path"] = "p" }, Factories[code].Error.Details);

    [Theory]
    [InlineData(ErrorCode.SteamLibraryUnreadable)]
    [InlineData(ErrorCode.SteamManifestUnreadable)]
    [InlineData(ErrorCode.SteamNoCurrentAccount)]
    [InlineData(ErrorCode.SteamLocalConfigMalformed)]
    [InlineData(ErrorCode.SteamLocalConfigUnwritable)]
    [InlineData(ErrorCode.SteamStartFailed)]
    [InlineData(ErrorCode.WorkshopSubscriptionsUnreadable)]
    [InlineData(ErrorCode.WorkshopManifestUnreadable)]
    [InlineData(ErrorCode.WorkshopSteamApiUnusable)]
    [InlineData(ErrorCode.WorkshopHelperStartFailed)]
    [InlineData(ErrorCode.InstallPathUnreadable)]
    [InlineData(ErrorCode.JavaSourceUnreadable)]
    [InlineData(ErrorCode.JavaReleaseUnreadable)]
    [InlineData(ErrorCode.LaunchConfigMalformed)]
    [InlineData(ErrorCode.LaunchConfigWriteFailed)]
    [InlineData(ErrorCode.LaunchStartFailed)]
    [InlineData(ErrorCode.ZombieBuddyCopyFailed)]
    [InlineData(ErrorCode.ZombieBuddyDeleteFailed)]
    [InlineData(ErrorCode.ModCollectionFileUnreadable)]
    [InlineData(ErrorCode.ModCollectionFileMalformed)]
    [InlineData(ErrorCode.ModCollectionFileWriteFailed)]
    [InlineData(ErrorCode.UpdateInstallFolderNotWritable)]
    [InlineData(ErrorCode.UpdateInstallFailed)]
    [InlineData(ErrorCode.UpdateRestartFailed)]
    public void Factory_PathReasonDetails_HoldThePathAndReason(ErrorCode code) =>
        Assert.Equal(new Dictionary<string, string> { ["path"] = "p", ["reason"] = "r" }, Factories[code].Error.Details);

    [Theory]
    [InlineData(ErrorCode.SteamVdfMalformed)]
    [InlineData(ErrorCode.SteamShutdownUnconfirmed)]
    [InlineData(ErrorCode.WorkshopNetworkFailed)]
    [InlineData(ErrorCode.WorkshopResponseInvalid)]
    [InlineData(ErrorCode.WorkshopSteamInitFailed)]
    [InlineData(ErrorCode.WorkshopHelperFailed)]
    [InlineData(ErrorCode.WorkshopHelperRequestRejected)]
    [InlineData(ErrorCode.WorkshopCollectionsQueryFailed)]
    public void Factory_ReasonDetails_HoldTheReason(ErrorCode code) =>
        Assert.Equal(new Dictionary<string, string> { ["reason"] = "r" }, Factories[code].Error.Details);
}
