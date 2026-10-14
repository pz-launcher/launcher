using System;
using System.Globalization;
using PzLauncher.Core.Common;

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
            ErrorCode.SteamNotInstalled => Strings.Error_SteamNotInstalled,
            ErrorCode.SteamLibraryUnreadable => Format(Strings.Error_SteamLibraryUnreadable, path),
            ErrorCode.SteamManifestUnreadable => Format(Strings.Error_SteamManifestUnreadable, path),
            ErrorCode.SteamVdfMalformed => Strings.Error_SteamVdfMalformed,
            ErrorCode.InstallFilesMissing => Format(Strings.Error_InstallFilesMissing, path),
            ErrorCode.InstallInvalidPath => Strings.Error_InstallInvalidPath,
            ErrorCode.InstallPathNotFound => Format(Strings.Error_InstallPathNotFound, path),
            ErrorCode.InstallPathUnreadable => Format(Strings.Error_InstallPathUnreadable, path),
            ErrorCode.InstallNotAPzInstall => Format(Strings.Error_InstallNotAPzInstall, path),
            ErrorCode.ResultUninitialized => Strings.Error_ResultUninitialized,
        };
#pragma warning restore CS8524
    }

    public static string FolderNotLocal(string location) => Format(Strings.Error_FolderNotLocal, location);

    private static string Format(string format, string value) =>
        string.Format(CultureInfo.CurrentCulture, format, value);

    private static string Detail(Error error, string key) =>
        error.Details.TryGetValue(key, out var value) ? value : "?";
}
