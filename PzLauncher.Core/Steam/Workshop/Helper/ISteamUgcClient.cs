namespace PzLauncher.Core.Steam.Workshop.Helper;

/// <summary>The Steamworks calls a <see cref="SteamHelperSession"/> makes, so tests can stand in for Steam.</summary>
internal interface ISteamUgcClient
{
    /// <summary>Callback structs use pack 8 on Windows and pack 4 elsewhere (VALVE_CALLBACK_PACK_LARGE / SMALL).</summary>
    bool LargeCallbackPack { get; }

    SteamItemState GetItemState(ulong id);

    /// <returns>(0, 0) when Steam has no download info for the item.</returns>
    (ulong Downloaded, ulong Total) GetItemDownloadInfo(ulong id);

    /// <returns>The call handle, 0 when Steam refused the call.</returns>
    ulong SubscribeItem(ulong id);

    /// <returns>The call handle, 0 when Steam refused the call.</returns>
    ulong UnsubscribeItem(ulong id);

    bool DownloadItem(ulong id, bool highPriority);

    /// <summary>The signed-in account: the low 32 bits of its SteamID64.</summary>
    uint GetAccountId();

    /// <summary>One page of the account's collections of the game, with child counts.</summary>
    /// <returns>The query handle, <see cref="SteamUgcQuery.InvalidHandle"/> when Steam refused it; release every other one.</returns>
    ulong CreateUserCollectionsQuery(uint accountId, SteamUserUgcList list, uint page);

    /// <returns>The call handle, 0 when Steam refused the call.</returns>
    ulong SendQuery(ulong handle);

    /// <returns><c>null</c> when Steam has no result at this index.</returns>
    UgcDetails? GetQueryResult(ulong handle, uint index);

    void ReleaseQuery(ulong handle);

    /// <summary>Runs one dispatch frame and returns every callback and finished call result it produced.</summary>
    List<SteamCallback> Pump();
}
