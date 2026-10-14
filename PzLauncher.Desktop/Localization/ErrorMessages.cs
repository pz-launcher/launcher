using System;
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
            ErrorCode.SteamNotInstalled =>
                "Steam doesn't seem to be installed, so no installs were found automatically. Use \"Add folder…\" to add one by hand.",
            ErrorCode.SteamLibraryUnreadable =>
                $"Couldn't read the Steam library list ({path}). Installs in other Steam libraries may be missing.",
            ErrorCode.SteamManifestUnreadable =>
                $"Couldn't read a Steam app manifest ({path}). That install is skipped.",
            ErrorCode.SteamVdfMalformed =>
                "A Steam file is damaged and couldn't be read.",
            ErrorCode.InstallFilesMissing =>
                $"Steam lists an install at {path}, but the folder is missing. Try verifying the game files in Steam.",
            ErrorCode.InstallInvalidPath =>
                "That isn't a valid folder path.",
            ErrorCode.InstallPathNotFound =>
                $"The folder {path} doesn't exist.",
            ErrorCode.InstallPathUnreadable =>
                $"The folder {path} couldn't be read. Check that you have permission to open it.",
            ErrorCode.InstallNotAPzInstall =>
                $"{path} doesn't look like a Project Zomboid game or dedicated server folder.",
            ErrorCode.ResultUninitialized =>
                "Something went wrong inside the launcher. Please report this as a bug.",
        };
#pragma warning restore CS8524
    }

    public static string FolderNotLocal(string location) =>
        $"{location} isn't a folder on this computer, so the launcher can't open it. Pick a local folder instead.";

    private static string Detail(Error error, string key) =>
        error.Details.TryGetValue(key, out var value) ? value : "?";
}
