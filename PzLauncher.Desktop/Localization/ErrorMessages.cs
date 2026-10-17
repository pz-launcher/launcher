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
            ErrorCode.ZabricNotPreloadable => Format(UiText.Error_ZabricNotPreloadable, path, Detail(error, "reason")),
            ErrorCode.ZabricPreloadConfigInvalid => Format(UiText.Error_ZabricPreloadConfigInvalid, path),
            ErrorCode.ZabricPreloadConfigIoFailed => Format(UiText.Error_ZabricPreloadConfigIoFailed, path, Detail(error, "reason")),
            ErrorCode.ResultUninitialized => UiText.Error_ResultUninitialized,
        };
    }

    public static string FolderNotLocal(string location) => Format(UiText.Error_FolderNotLocal, location);

    private static string Format(string format, string value) =>
        string.Format(CultureInfo.CurrentCulture, format, value);

    private static string Format(string format, string first, string second) =>
        string.Format(CultureInfo.CurrentCulture, format, first, second);

    private static string Detail(Error error, string key) =>
        error.Details.TryGetValue(key, out var value) ? value : "?";
}
