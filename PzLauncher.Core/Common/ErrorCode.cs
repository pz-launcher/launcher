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
    [JsonStringEnumMemberName("zombiebuddy.config_write_failed")] ZombieBuddyConfigWriteFailed,
    [JsonStringEnumMemberName("zombiebuddy.not_initialized")] ZombieBuddyNotInitialized,
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
