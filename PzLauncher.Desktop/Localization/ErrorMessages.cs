using System.Globalization;
using PzLauncher.Core.Common;
using PzLauncher.Desktop.Lang;

namespace PzLauncher.Desktop.Localization;

/// <summary>The only place that turns an <see cref="Error"/> into user-facing text.</summary>
public static class ErrorMessages
{
    public static string ToUserText(Error error)
    {
        var path = Detail(error, "path");
        return error.Code switch
        {
            ErrorCode.SteamNotInstalled => UiText.Error_SteamNotInstalled,
            ErrorCode.SteamLibraryUnreadable => Format(UiText.Error_SteamLibraryUnreadable, path),
            ErrorCode.SteamManifestUnreadable => Format(UiText.Error_SteamManifestUnreadable, path),
            ErrorCode.SteamVdfMalformed => UiText.Error_SteamVdfMalformed,
            ErrorCode.InstallFilesMissing => Format(UiText.Error_InstallFilesMissing, path),
            ErrorCode.InstallInvalidPath => UiText.Error_InstallInvalidPath,
            ErrorCode.InstallPathNotFound => Format(UiText.Error_InstallPathNotFound, path),
            ErrorCode.InstallPathUnreadable => Format(UiText.Error_InstallPathUnreadable, path),
            ErrorCode.InstallNotAPzInstall => Format(UiText.Error_InstallNotAPzInstall, path),
            ErrorCode.JavaInvalidPath => UiText.Error_JavaInvalidPath,
            ErrorCode.JavaPathNotFound => Format(UiText.Error_JavaPathNotFound, path),
            ErrorCode.JavaNotAJavaHome => Format(UiText.Error_JavaNotAJavaHome, path),
            ErrorCode.JavaNot64Bit => Format(UiText.Error_JavaNot64Bit, path),
            ErrorCode.JavaSourceUnreadable => Format(UiText.Error_JavaSourceUnreadable, path),
            ErrorCode.JavaReleaseUnreadable => Format(UiText.Error_JavaReleaseUnreadable, path),
            ErrorCode.LaunchPlatformUnsupported => UiText.Error_LaunchPlatformUnsupported,
            ErrorCode.LaunchInvalidMemory => Format(UiText.Error_LaunchInvalidMemory, Detail(error, "value")),
            ErrorCode.LaunchAlreadyRunning => UiText.Error_LaunchAlreadyRunning,
            ErrorCode.LaunchNotAGame => Format(UiText.Error_LaunchNotAGame, path),
            ErrorCode.LaunchConfigMissing => Format(UiText.Error_LaunchConfigMissing, path),
            ErrorCode.LaunchConfigMalformed => Format(UiText.Error_LaunchConfigMalformed, path),
            ErrorCode.LaunchJavaMissing => Format(UiText.Error_LaunchJavaMissing, path),
            ErrorCode.LaunchStartFailed => Format(UiText.Error_LaunchStartFailed, path),
            ErrorCode.LaunchGcIncompatible => Format(UiText.Error_LaunchGcIncompatible, path),
            ErrorCode.LaunchGcIncompatibleViewpoint => UiText.Error_LaunchGcIncompatibleViewpoint,
            ErrorCode.LaunchGameCrashed => Format(UiText.Error_LaunchGameCrashed, path, Detail(error, "exitCode")),
            ErrorCode.LaunchGameExitedEarly => Format(UiText.Error_LaunchGameExitedEarly, Detail(error, "exitCode"), Detail(error, "output")),
            ErrorCode.LaunchGcConflict => Format(UiText.Error_LaunchGcConflict, Detail(error, "value")),
            ErrorCode.ZombieBuddyNotSubscribed => Format(UiText.Error_ZombieBuddyNotSubscribed, Detail(error, "itemId")),
            ErrorCode.ZombieBuddySourceFileMissing => Format(UiText.Error_ZombieBuddySourceFileMissing, path),
            ErrorCode.ZombieBuddyCopyFailed => Format(UiText.Error_ZombieBuddyCopyFailed, path),
            ErrorCode.ZombieBuddyDeleteFailed => Format(UiText.Error_ZombieBuddyDeleteFailed, path),
            ErrorCode.ZombieBuddyConfigWriteFailed => Format(UiText.Error_ZombieBuddyConfigWriteFailed, path),
            ErrorCode.ZombieBuddyNotInitialized => UiText.Error_ZombieBuddyNotInitialized,
            ErrorCode.ZombieBuddyModNotPreloadable => Format(UiText.Error_ZombieBuddyModNotPreloadable, path, Detail(error, "reason"), Detail(error, "modId")),
            ErrorCode.ZombieBuddyPreloadConfigInvalid => Format(UiText.Error_ZombieBuddyPreloadConfigInvalid, path, Detail(error, "modId")),
            ErrorCode.ZombieBuddyPreloadConfigIoFailed => Format(UiText.Error_ZombieBuddyPreloadConfigIoFailed, path, Detail(error, "reason"), Detail(error, "modId")),
            ErrorCode.ModCollectionCacheFolderUnresolved => Format(UiText.Error_ModCollectionCacheFolderUnresolved, path),
            ErrorCode.ModCollectionNameInvalid => Format(UiText.Error_ModCollectionNameInvalid, Detail(error, "name")),
            ErrorCode.ModCollectionModIdInvalid => Format(UiText.Error_ModCollectionModIdInvalid, Detail(error, "modId")),
            ErrorCode.ModCollectionNotFound => Format(UiText.Error_ModCollectionNotFound, Detail(error, "name")),
            ErrorCode.ModCollectionFileUnreadable => Format(UiText.Error_ModCollectionFileUnreadable, path),
            ErrorCode.ModCollectionFileMalformed => Format(UiText.Error_ModCollectionFileMalformed, path),
            ErrorCode.ModCollectionFileWriteFailed => Format(UiText.Error_ModCollectionFileWriteFailed, path),
            ErrorCode.UpdateNetworkFailed => Format(UiText.Error_UpdateNetworkFailed, Detail(error, "source")),
            ErrorCode.UpdateReleaseInfoInvalid => Format(UiText.Error_UpdateReleaseInfoInvalid, Detail(error, "source")),
            ErrorCode.UpdatePackageInvalid => Format(UiText.Error_UpdatePackageInvalid, Detail(error, "package")),
            ErrorCode.UpdateInstallFolderNotWritable => Format(UiText.Error_UpdateInstallFolderNotWritable, path),
            ErrorCode.UpdateInstallFailed => Format(UiText.Error_UpdateInstallFailed, path),
            ErrorCode.UpdateDevelopmentBuild => UiText.Error_UpdateDevelopmentBuild,
            ErrorCode.UpdateRestartFailed => Format(UiText.Error_UpdateRestartFailed, path),
            ErrorCode.NetworkProxyHostInvalid => UiText.Error_NetworkProxyHostInvalid,
            ErrorCode.NetworkProxyPortInvalid => UiText.Error_NetworkProxyPortInvalid,
            ErrorCode.NetworkUnreachable => Format(UiText.Error_NetworkUnreachable, Detail(error, "target"), Detail(error, "reason")),
            ErrorCode.NetworkTimeout => Format(UiText.Error_NetworkTimeout, Detail(error, "target"), Detail(error, "seconds")),
            ErrorCode.ResultUninitialized => UiText.Error_ResultUninitialized,
        };
    }

    public static string FolderNotLocal(string location) => Format(UiText.Error_FolderNotLocal, location);

    private static string Format(string format, string value) =>
        string.Format(CultureInfo.CurrentCulture, format, value);

    private static string Format(string format, string first, string second) =>
        string.Format(CultureInfo.CurrentCulture, format, first, second);

    private static string Format(string format, string first, string second, string third) =>
        string.Format(CultureInfo.CurrentCulture, format, first, second, third);

    private static string Detail(Error error, string key) =>
        error.Details.TryGetValue(key, out var value) ? value : "?";
}
