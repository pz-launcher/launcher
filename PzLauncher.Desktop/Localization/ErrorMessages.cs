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
            ErrorCode.ResultUninitialized => UiText.Error_ResultUninitialized,
        };
    }

    public static string FolderNotLocal(string location) => Format(UiText.Error_FolderNotLocal, location);

    private static string Format(string format, string value) =>
        string.Format(CultureInfo.CurrentCulture, format, value);

    private static string Detail(Error error, string key) =>
        error.Details.TryGetValue(key, out var value) ? value : "?";
}
