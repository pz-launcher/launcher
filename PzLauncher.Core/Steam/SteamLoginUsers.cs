using System.Globalization;
using PzLauncher.Core.Common;

namespace PzLauncher.Core.Steam;

internal sealed record SteamLogin(string SteamRoot, SteamAccount Account);

/// <summary>The account Steam signs in with: <c>config/loginusers.vdf</c>'s <c>MostRecent</c> user, else the latest sign-in.</summary>
internal static class SteamLoginUsers
{
    /// <summary>The current account of the first Steam root found.</summary>
    public static async Task<Result<SteamLogin>> LocateCurrentAsync(SteamLocator locator, CancellationToken ct)
    {
        if (locator.FindSteamRoots() is not [var root, ..])
        {
            return SteamErrors.NotInstalled();
        }

        var account = await ReadCurrentAsync(SteamClientFiles.LoginUsersPath(root), ct).ConfigureAwait(false);
        return account.IsSuccess ? new SteamLogin(root, account.Value) : account.Error;
    }

    public static async Task<Result<SteamAccount>> ReadCurrentAsync(string path, CancellationToken ct)
    {
        var text = await ReadFile.TextAsync(path, reason => SteamErrors.NoCurrentAccount(path, reason), ct).ConfigureAwait(false);
        return text.IsSuccess ? ParseCurrent(text.Value, path) : text.Error;
    }

    // Newer clients dropped MostRecent; each sign-in still updates the user's timestamp.
    public static Result<SteamAccount> ParseCurrent(string text, string path)
    {
        var users = SteamKeyValues.ParseBlock(text, "users", reason => SteamErrors.NoCurrentAccount(path, reason));
        if (!users.IsSuccess)
        {
            return users.Error;
        }

        (ulong SteamId, ValveKeyValue.KVObject User, long Timestamp)? latest = null;
        foreach (var (key, user) in users.Value.GetChildren())
        {
            if (!user.IsCollection || !ulong.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out var steamId))
            {
                continue;
            }

            if (user.GetString(SteamClientFiles.MostRecentKey) == "1")
            {
                return ToAccount(steamId, user);
            }

            var timestamp = long.TryParse(user.GetString(SteamClientFiles.TimestampKey), NumberStyles.None, CultureInfo.InvariantCulture, out var value)
                ? value
                : -1;
            if (latest is null || timestamp > latest.Value.Timestamp)
            {
                latest = (steamId, user, timestamp);
            }
        }

        return latest is { } found ? ToAccount(found.SteamId, found.User) : SteamErrors.NoCurrentAccount(path, "no users");
    }

    private static SteamAccount ToAccount(ulong steamId, ValveKeyValue.KVObject user)
    {
        var accountId = (uint)(steamId & 0xFFFFFFFF);
        var name = user.GetString(SteamClientFiles.PersonaNameKey) is { Length: > 0 } persona ? persona
            : user.GetString(SteamClientFiles.AccountNameKey) is { Length: > 0 } login ? login
            : accountId.ToString(CultureInfo.InvariantCulture);
        return new SteamAccount(accountId, name);
    }
}
