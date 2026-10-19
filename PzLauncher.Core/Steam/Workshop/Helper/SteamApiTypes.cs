using System.Buffers.Binary;

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
}

internal readonly record struct PublishedFileResult(int Result, ulong Id);

internal readonly record struct DownloadItemResult(uint AppId, ulong Id, int Result);

/// <summary>Callback payload layouts; a 64-bit field after a 32-bit one starts at 8 with pack 8, at 4 with pack 4.</summary>
internal static class SteamCallbackPayloads
{
    public const int ResultOk = 1;

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
}
