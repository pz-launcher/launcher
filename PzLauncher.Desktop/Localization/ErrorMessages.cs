using System;
using System.Globalization;
using PzLauncher.Core.Common;
using PzLauncher.Desktop.Lang;

namespace PzLauncher.Desktop.Localization;

/// <summary>The only place that turns an <see cref="Error"/> into user-facing text.</summary>
public static class ErrorMessages
{
    public static string ToUserText(Error error)
    {
        if (!Enum.IsDefined(error.Code))
        {
            return error.Message;
        }

        var path = Detail(error, "path");
#pragma warning disable CS8524 // Unnamed values are handled above. No discard arm on purpose: CS8509 makes a new ErrorCode without user text a build error.
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
            ErrorCode.ResultUninitialized => UiText.Error_ResultUninitialized,
        };
#pragma warning restore CS8524
    }

    public static string FolderNotLocal(string location) => Format(UiText.Error_FolderNotLocal, location);

    private static string Format(string format, string value) =>
        string.Format(CultureInfo.CurrentCulture, format, value);

    private static string Detail(Error error, string key) =>
        error.Details.TryGetValue(key, out var value) ? value : "?";
}
