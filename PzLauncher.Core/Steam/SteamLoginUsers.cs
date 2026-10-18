using System.Globalization;
using PzLauncher.Core.Common;

namespace PzLauncher.Core.Steam;

/// <summary>The account Steam signs in with: <c>config/loginusers.vdf</c>'s <c>MostRecent</c> user, else the latest sign-in.</summary>
internal static class SteamLoginUsers
{
    public static async Task<Result<SteamAccount>> ReadCurrentAsync(string path, CancellationToken ct)
    {
        string text;
        try
        {
            text = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return SteamErrors.NoCurrentAccount(path, ex.Message);
        }

        return ParseCurrent(text, path);
    }

    // Newer clients dropped MostRecent; each sign-in still updates the user's timestamp.
    public static Result<SteamAccount> ParseCurrent(string text, string path)
    {
        var parsed = SteamKeyValues.Parse(text);
        if (!parsed.IsSuccess)
        {
            return SteamErrors.NoCurrentAccount(path, parsed.Error.Message);
        }

        var users = parsed.Value.GetChild("users");
        if (users is null || !users.IsCollection)
        {
            return SteamErrors.NoCurrentAccount(path, "missing \"users\" block");
        }

        (ulong SteamId, ValveKeyValue.KVObject User, long Timestamp)? latest = null;
        foreach (var (key, user) in users.GetChildren())
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
