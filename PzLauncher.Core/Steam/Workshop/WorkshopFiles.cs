using System.Globalization;
using PzLauncher.Core.Launch;
using PzLauncher.Core.Platform;

namespace PzLauncher.Core.Steam.Workshop;

/// <summary>The Steam client's Workshop files for the game and the keyless Steam Web API methods.</summary>
internal static class WorkshopFiles
{
    public const string SubscriptionsRoot = "subscribedfiles";

    public const string PublishedFileIdKey = "publishedfileid";

    public const string TimeSubscribedKey = "time_subscribed";

    public const string DisabledLocallyKey = "disabled_locally";

    public const string ManifestRoot = "AppWorkshop";

    public const string InstalledItemsKey = "WorkshopItemsInstalled";

    public const string ItemDetailsKey = "WorkshopItemDetails";

    public const string SizeKey = "size";

    public const string TimeUpdatedKey = "timeupdated";

    public const string ManifestKey = "manifest";

    public const string LatestManifestKey = "latest_manifest";

    public const string PublishedFileDetailsUrl = "https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/";

    public const string CollectionDetailsUrl = "https://api.steampowered.com/ISteamRemoteStorage/GetCollectionDetails/v1/";

    // 1000 ids in one request was tested to work; the API documents no limit.
    public const int MaxIdsPerRequest = 1000;

    /// <summary>k_EResultOK; an item or collection the API doesn't know or hides answers 9 (k_EResultFileNotFound).</summary>
    public const int ResultOk = 1;

    /// <summary>EWorkshopFileType's k_EWorkshopFileTypeCollection in a collection's children.</summary>
    public const int FileTypeCollection = 2;

    /// <summary>Item and collection pages: <c>https://steamcommunity.com/sharedfiles/filedetails/?id=&lt;id&gt;</c>, also under <c>/workshop/</c>.</summary>
    public const string CommunityHost = "steamcommunity.com";

    public const string CommunityIdParameter = "id";

    /// <summary>Opens an item's page inside the Steam client.</summary>
    public const string ClientPageUrl = "steam://url/CommunityFilePage/";

    public const string WindowsSteamApi = "steam_api64.dll";

    public const string LinuxSteamApi = "libsteam_api.so";

    /// <summary>Steam reads the app id of a process it didn't start from these.</summary>
    public static IReadOnlyList<string> AppIdVariables { get; } = ["SteamAppId", "SteamGameId"];

    /// <summary>Where the game keeps its own <c>steam_api</c>, in lookup order; empty where no location is known (macOS).</summary>
    public static IReadOnlyList<string> SteamApiCandidates(string installRoot, PlatformKind os) => os switch
    {
        PlatformKind.Windows => [Path.Combine(installRoot, WindowsSteamApi)],

        // Both folders are on the loader path the game runs with (LinuxLaunchEnvironment).
        PlatformKind.Linux => [
            Path.Combine(PzLaunchFiles.ResolveGameFolder(installRoot), LinuxSteamApi),
            Path.Combine(PzLaunchFiles.ResolveGameFolder(installRoot), PzLaunchFiles.NativesFolder, LinuxSteamApi)],
        PlatformKind.MacOS => [],
    };

    public static string SubscriptionsPath(string steamRoot, uint accountId) =>
        Path.Combine(SteamClientFiles.UserDataFolder(steamRoot, accountId), "ugc", $"{GameId}_subscriptions.vdf");

    // The library's file; a stale copy inside the game folder is never read.
    public static string ManifestPath(string library) => Path.Combine(LibraryWorkshopFolder(library), $"appworkshop_{GameId}.acf");

    /// <summary>Where Steam installs the game's Workshop items in a library, one folder per item id.</summary>
    public static string ContentFolder(string library) => Path.Combine(LibraryWorkshopFolder(library), "content", GameId);

    public static string ItemFolder(string library, string itemId) => Path.Combine(ContentFolder(library), itemId);

    private static string GameId => PzSteamApps.Game.ToString(CultureInfo.InvariantCulture);

    private static string LibraryWorkshopFolder(string library) => Path.Combine(library, "steamapps", "workshop");
}
