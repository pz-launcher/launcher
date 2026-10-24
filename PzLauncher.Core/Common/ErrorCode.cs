using System.Collections.Frozen;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using PzLauncher.Core.Serialization;

namespace PzLauncher.Core.Common;

// Wire names (JSON, logs, HTTP) are a public contract: never change one. Members may be renamed;
// numeric values are never persisted or sent.
public enum ErrorCode
{
    // ── result ────────────────────────────────────────────────
    // A default-constructed Result. Always a bug.
    [JsonStringEnumMemberName("result.uninitialized")] ResultUninitialized,

    // ── steam ─────────────────────────────────────────────────
    [JsonStringEnumMemberName("steam.not_installed")] SteamNotInstalled,
    [JsonStringEnumMemberName("steam.library_unreadable")] SteamLibraryUnreadable,
    [JsonStringEnumMemberName("steam.manifest_unreadable")] SteamManifestUnreadable,
    [JsonStringEnumMemberName("steam.vdf_malformed")] SteamVdfMalformed,
    [JsonStringEnumMemberName("steam.platform_unsupported")] SteamPlatformUnsupported,
    [JsonStringEnumMemberName("steam.no_current_account")] SteamNoCurrentAccount,
    [JsonStringEnumMemberName("steam.app_not_in_local_config")] SteamAppNotInLocalConfig,
    [JsonStringEnumMemberName("steam.local_config_malformed")] SteamLocalConfigMalformed,
    [JsonStringEnumMemberName("steam.local_config_unwritable")] SteamLocalConfigUnwritable,
    [JsonStringEnumMemberName("steam.client_running")] SteamClientRunning,
    [JsonStringEnumMemberName("steam.shutdown_timed_out")] SteamShutdownTimedOut,
    [JsonStringEnumMemberName("steam.shutdown_unconfirmed")] SteamShutdownUnconfirmed,
    [JsonStringEnumMemberName("steam.start_failed")] SteamStartFailed,

    // ── workshop ──────────────────────────────────────────────
    [JsonStringEnumMemberName("workshop.subscriptions_unreadable")] WorkshopSubscriptionsUnreadable,
    [JsonStringEnumMemberName("workshop.manifest_unreadable")] WorkshopManifestUnreadable,
    [JsonStringEnumMemberName("workshop.network_failed")] WorkshopNetworkFailed,
    [JsonStringEnumMemberName("workshop.response_invalid")] WorkshopResponseInvalid,
    [JsonStringEnumMemberName("workshop.collection_not_found")] WorkshopCollectionNotFound,
    [JsonStringEnumMemberName("workshop.steam_api_missing")] WorkshopSteamApiMissing,
    [JsonStringEnumMemberName("workshop.steam_api_unusable")] WorkshopSteamApiUnusable,
    [JsonStringEnumMemberName("workshop.steam_not_running")] WorkshopSteamNotRunning,
    [JsonStringEnumMemberName("workshop.steam_init_failed")] WorkshopSteamInitFailed,
    [JsonStringEnumMemberName("workshop.helper_start_failed")] WorkshopHelperStartFailed,
    [JsonStringEnumMemberName("workshop.helper_failed")] WorkshopHelperFailed,
    [JsonStringEnumMemberName("workshop.helper_request_rejected")] WorkshopHelperRequestRejected,
    [JsonStringEnumMemberName("workshop.helper_timed_out")] WorkshopHelperTimedOut,
    [JsonStringEnumMemberName("workshop.collections_query_failed")] WorkshopCollectionsQueryFailed,

    // ── install ───────────────────────────────────────────────
    [JsonStringEnumMemberName("install.files_missing")] InstallFilesMissing,
    [JsonStringEnumMemberName("install.invalid_path")] InstallInvalidPath,
    [JsonStringEnumMemberName("install.path_not_found")] InstallPathNotFound,
    [JsonStringEnumMemberName("install.path_unreadable")] InstallPathUnreadable,
    [JsonStringEnumMemberName("install.not_a_pz_install")] InstallNotAPzInstall,

    // ── java ──────────────────────────────────────────────────
    [JsonStringEnumMemberName("java.invalid_path")] JavaInvalidPath,
    [JsonStringEnumMemberName("java.path_not_found")] JavaPathNotFound,
    [JsonStringEnumMemberName("java.not_a_java_home")] JavaNotAJavaHome,
    [JsonStringEnumMemberName("java.not_64_bit")] JavaNot64Bit,
    [JsonStringEnumMemberName("java.source_unreadable")] JavaSourceUnreadable,
    [JsonStringEnumMemberName("java.release_unreadable")] JavaReleaseUnreadable,

    // ── launch ────────────────────────────────────────────────
    [JsonStringEnumMemberName("launch.platform_unsupported")] LaunchPlatformUnsupported,
    [JsonStringEnumMemberName("launch.invalid_memory")] LaunchInvalidMemory,
    [JsonStringEnumMemberName("launch.already_running")] LaunchAlreadyRunning,
    [JsonStringEnumMemberName("launch.not_a_game")] LaunchNotAGame,
    [JsonStringEnumMemberName("launch.config_missing")] LaunchConfigMissing,
    [JsonStringEnumMemberName("launch.config_malformed")] LaunchConfigMalformed,
    [JsonStringEnumMemberName("launch.config_write_failed")] LaunchConfigWriteFailed,
    [JsonStringEnumMemberName("launch.java_missing")] LaunchJavaMissing,
    [JsonStringEnumMemberName("launch.start_failed")] LaunchStartFailed,
    [JsonStringEnumMemberName("launch.gc_incompatible")] LaunchGcIncompatible,
    [JsonStringEnumMemberName("launch.gc_incompatible_viewpoint")] LaunchGcIncompatibleViewpoint,
    [JsonStringEnumMemberName("launch.game_crashed")] LaunchGameCrashed,
    [JsonStringEnumMemberName("launch.game_exited_early")] LaunchGameExitedEarly,
    [JsonStringEnumMemberName("launch.gc_conflict")] LaunchGcConflict,

    // ── zombiebuddy ───────────────────────────────────────────
    [JsonStringEnumMemberName("zombiebuddy.not_subscribed")] ZombieBuddyNotSubscribed,
    [JsonStringEnumMemberName("zombiebuddy.source_file_missing")] ZombieBuddySourceFileMissing,
    [JsonStringEnumMemberName("zombiebuddy.copy_failed")] ZombieBuddyCopyFailed,
    [JsonStringEnumMemberName("zombiebuddy.delete_failed")] ZombieBuddyDeleteFailed,
    [JsonStringEnumMemberName("zombiebuddy.not_initialized")] ZombieBuddyNotInitialized,
    [JsonStringEnumMemberName("zombiebuddy.mod_not_preloadable")] ZombieBuddyModNotPreloadable,
    [JsonStringEnumMemberName("zombiebuddy.preload_config_invalid")] ZombieBuddyPreloadConfigInvalid,
    [JsonStringEnumMemberName("zombiebuddy.preload_config_io_failed")] ZombieBuddyPreloadConfigIoFailed,

    // ── modcollection ─────────────────────────────────────────
    [JsonStringEnumMemberName("modcollection.cache_folder_unresolved")] ModCollectionCacheFolderUnresolved,
    [JsonStringEnumMemberName("modcollection.name_invalid")] ModCollectionNameInvalid,
    [JsonStringEnumMemberName("modcollection.mod_id_invalid")] ModCollectionModIdInvalid,
    [JsonStringEnumMemberName("modcollection.not_found")] ModCollectionNotFound,
    [JsonStringEnumMemberName("modcollection.file_unreadable")] ModCollectionFileUnreadable,
    [JsonStringEnumMemberName("modcollection.file_malformed")] ModCollectionFileMalformed,
    [JsonStringEnumMemberName("modcollection.file_write_failed")] ModCollectionFileWriteFailed,

    // ── update ────────────────────────────────────────────────
    [JsonStringEnumMemberName("update.network_failed")] UpdateNetworkFailed,
    [JsonStringEnumMemberName("update.release_info_invalid")] UpdateReleaseInfoInvalid,
    [JsonStringEnumMemberName("update.package_invalid")] UpdatePackageInvalid,
    [JsonStringEnumMemberName("update.install_folder_not_writable")] UpdateInstallFolderNotWritable,
    [JsonStringEnumMemberName("update.install_failed")] UpdateInstallFailed,
    [JsonStringEnumMemberName("update.development_build")] UpdateDevelopmentBuild,
    [JsonStringEnumMemberName("update.restart_failed")] UpdateRestartFailed,

    // ── network ───────────────────────────────────────────────
    [JsonStringEnumMemberName("network.proxy_host_invalid")] NetworkProxyHostInvalid,
    [JsonStringEnumMemberName("network.proxy_port_invalid")] NetworkProxyPortInvalid,
    [JsonStringEnumMemberName("network.unreachable")] NetworkUnreachable,
    [JsonStringEnumMemberName("network.timeout")] NetworkTimeout,

    // ── server ────────────────────────────────────────────────
    [JsonStringEnumMemberName("server.name_invalid")] ServerNameInvalid,
    [JsonStringEnumMemberName("server.already_exists")] ServerAlreadyExists,
    [JsonStringEnumMemberName("server.not_found")] ServerNotFound,
    [JsonStringEnumMemberName("server.not_a_server_install")] ServerNotAServerInstall,
    [JsonStringEnumMemberName("server.already_running")] ServerAlreadyRunning,
    [JsonStringEnumMemberName("server.not_running")] ServerNotRunning,
    [JsonStringEnumMemberName("server.not_ready")] ServerNotReady,
    [JsonStringEnumMemberName("server.admin_password_required")] ServerAdminPasswordRequired,
    [JsonStringEnumMemberName("server.start_failed")] ServerStartFailed,
    [JsonStringEnumMemberName("server.exited_early")] ServerExitedEarly,
    [JsonStringEnumMemberName("server.exited_unexpectedly")] ServerExitedUnexpectedly,
    [JsonStringEnumMemberName("server.rcon_failed")] ServerRconFailed,
    [JsonStringEnumMemberName("server.command_invalid")] ServerCommandInvalid,
    [JsonStringEnumMemberName("server.stop_timeout")] ServerStopTimeout,
    [JsonStringEnumMemberName("server.file_access_failed")] ServerFileAccessFailed,
}

public static class ErrorCodeExtensions
{
    // Built from the JSON metadata so the attribute stays the single source of truth (AOT-safe, no reflection).
    private static readonly FrozenDictionary<ErrorCode, string> WireNames =
        Enum.GetValues<ErrorCode>().ToFrozenDictionary(
            code => code,
            code => JsonSerializer.Deserialize<string>(
                JsonSerializer.Serialize(code, CoreJsonContext.Default.ErrorCode), CoreJsonContext.Default.String)
                ?? throw new UnreachableException($"ErrorCode {code} serialized to JSON null."));

    /// <summary>Use this in logs, never <c>ToString()</c>.</summary>
    public static string ToWireName(this ErrorCode code) =>
        WireNames.TryGetValue(code, out var name) ? name : code.ToString();
}
