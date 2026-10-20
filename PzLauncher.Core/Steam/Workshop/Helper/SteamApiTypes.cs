using System.Buffers.Binary;
using System.Text;

namespace PzLauncher.Core.Steam.Workshop.Helper;

/// <summary>ESteamAPIInitResult.</summary>
internal enum SteamApiInitResult
{
    Ok = 0,
    FailedGeneric = 1,
    NoSteamClient = 2,
    VersionMismatch = 3,
}

/// <param name="Message">Steam's text; empty on success.</param>
internal readonly record struct SteamInitOutcome(SteamApiInitResult Result, string Message);

/// <summary>EItemState.</summary>
[Flags]
internal enum SteamItemState : uint
{
    None = 0,
    Subscribed = 1,
    LegacyItem = 2,
    Installed = 4,
    NeedsUpdate = 8,
    Downloading = 16,
    DownloadPending = 32,
    DisabledLocally = 64,
}

/// <param name="ApiCall">The call this result answers; 0 for a broadcast callback.</param>
/// <param name="Failed">Steam lost the call result (ESteamAPICallFailure); the payload is empty.</param>
internal readonly record struct SteamCallback(int Id, ulong ApiCall, bool Failed, byte[] Payload);

internal static class SteamCallbackIds
{
    public const int ApiCallCompleted = 703;
    public const int SubscribeItemResult = 1313;
    public const int UnsubscribeItemResult = 1315;
    public const int DownloadItemResult = 3406;
    public const int UgcQueryCompleted = 3401;
}

/// <summary>EUserUGCList members the helper queries.</summary>
internal enum SteamUserUgcList
{
    Published = 0,
    Favorited = 5,
}

internal static class SteamUgcQuery
{
    /// <summary>EUGCMatchingUGCType's k_EUGCMatchingUGCType_Collections.</summary>
    public const int MatchingCollections = 3;

    /// <summary>EUserUGCListSortOrder's k_EUserUGCListSortOrder_CreationOrderDesc.</summary>
    public const int SortCreationOrderDesc = 0;

    /// <summary>Website-made collections have creator app 766 and in-game ones the game's, so the query must not filter on it.</summary>
    public const uint AnyCreatorApp = 0;

    /// <summary>kNumUGCResultsPerPage.</summary>
    public const uint ResultsPerPage = 50;

    /// <summary>k_UGCQueryHandleInvalid.</summary>
    public const ulong InvalidHandle = ulong.MaxValue;
}

internal readonly record struct PublishedFileResult(int Result, ulong Id);

internal readonly record struct DownloadItemResult(uint AppId, ulong Id, int Result);

internal readonly record struct UgcQueryCompleted(ulong Handle, int Result, uint Returned, uint TotalMatching);

/// <param name="Title">Empty when Steam returned none.</param>
internal readonly record struct UgcDetails(ulong Id, int Result, int FileType, string Title, uint ChildCount);

/// <summary>Callback payload layouts; a 64-bit field after a 32-bit one starts at 8 with pack 8, at 4 with pack 4.</summary>
internal static class SteamCallbackPayloads
{
    private const int UgcTitleOffset = 24;
    private const int UgcTitleSize = 129;

    /// <summary>SteamUGCDetails_t's size with pack 8; it is 9772 with pack 4.</summary>
    public const int UgcDetailsSize = 9784;

    /// <summary>RemoteStorageSubscribePublishedFileResult_t or RemoteStorageUnsubscribePublishedFileResult_t (same layout); <c>null</c> when the payload is too short.</summary>
    public static PublishedFileResult? ReadPublishedFileResult(byte[] payload, bool largePack)
    {
        var idOffset = largePack ? 8 : 4;
        if (payload.Length < idOffset + 8)
        {
            return null;
        }

        return new PublishedFileResult(
            BinaryPrimitives.ReadInt32LittleEndian(payload),
            BinaryPrimitives.ReadUInt64LittleEndian(payload.AsSpan(idOffset)));
    }

    /// <summary>DownloadItemResult_t; <c>null</c> when the payload is too short.</summary>
    public static DownloadItemResult? ReadDownloadItemResult(byte[] payload, bool largePack)
    {
        var idOffset = largePack ? 8 : 4;
        if (payload.Length < idOffset + 12)
        {
            return null;
        }

        return new DownloadItemResult(
            BinaryPrimitives.ReadUInt32LittleEndian(payload),
            BinaryPrimitives.ReadUInt64LittleEndian(payload.AsSpan(idOffset)),
            BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(idOffset + 8)));
    }

    /// <summary>SteamUGCQueryCompleted_t, the same offsets under both packings; <c>null</c> when the payload is too short.</summary>
    public static UgcQueryCompleted? ReadUgcQueryCompleted(byte[] payload)
    {
        if (payload.Length < 20)
        {
            return null;
        }

        return new UgcQueryCompleted(
            BinaryPrimitives.ReadUInt64LittleEndian(payload),
            BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(8)),
            BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(12)),
            BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(16)));
    }

    /// <summary>SteamUGCDetails_t, packed like the callbacks; <c>null</c> when the buffer is too short.</summary>
    public static UgcDetails? ReadUgcDetails(ReadOnlySpan<byte> details, bool largePack)
    {
        // The 8000-byte description and the 1025-byte tags each shift the following 64-bit field by 4 under pack 8.
        var childCountOffset = largePack ? 9768 : 9760;
        if (details.Length < childCountOffset + 4)
        {
            return null;
        }

        var title = details.Slice(UgcTitleOffset, UgcTitleSize);
        var length = title.IndexOf((byte)0);
        return new UgcDetails(
            BinaryPrimitives.ReadUInt64LittleEndian(details),
            BinaryPrimitives.ReadInt32LittleEndian(details[8..]),
            BinaryPrimitives.ReadInt32LittleEndian(details[12..]),
            Encoding.UTF8.GetString(length < 0 ? title : title[..length]),
            BinaryPrimitives.ReadUInt32LittleEndian(details[childCountOffset..]));
    }
}
