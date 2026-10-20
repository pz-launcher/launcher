using System.Text.Json.Serialization;

namespace PzLauncher.Core.Steam.Workshop.Helper;

/// <summary>The one JSON line the helper reads from stdin.</summary>
/// <param name="Ids">Empty for <see cref="SteamHelperAction.ListCollections"/>.</param>
internal sealed record SteamHelperRequest(SteamHelperAction Action, string SteamApiPath, IReadOnlyList<ulong> Ids, SteamHelperTimeouts Timeouts);

/// <summary>The public Workshop actions under the same names, plus the read-only list.</summary>
internal enum SteamHelperAction
{
    Subscribe,
    Unsubscribe,
    Update,
    ListCollections,
}

/// <param name="CallResult">How long the helper waits for Steam to answer one call.</param>
/// <param name="Stall">How long an update may go without a change in progress or state before it fails.</param>
internal sealed record SteamHelperTimeouts(TimeSpan CallResult, TimeSpan Stall)
{
    public static SteamHelperTimeouts Default { get; } = new(TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(2));

    /// <summary>The longest the helper writes nothing while it works: one call's wait, then a stalled download.</summary>
    [JsonIgnore]
    public TimeSpan LongestSilence => CallResult + Stall;
}

internal enum SteamHelperFailure
{
    BadRequest,
    LibraryNotLoaded,
    ExportMissing,
    SteamNotRunning,
    VersionMismatch,
    InitFailed,

    /// <summary>Steam didn't answer a collection query with OK.</summary>
    QueryFailed,
}

internal enum SteamHelperItemFailure
{
    SubscribeFailed,
    UnsubscribeFailed,
    DownloadNotStarted,
    DownloadFailed,
    TimedOut,
}

/// <summary>One JSON line the helper writes to stdout.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "event")]
[JsonDerivedType(typeof(SteamHelperStarted), "started")]
[JsonDerivedType(typeof(SteamHelperProgress), "progress")]
[JsonDerivedType(typeof(SteamHelperItemDone), "itemDone")]
[JsonDerivedType(typeof(SteamHelperItemFailed), "itemFailed")]
[JsonDerivedType(typeof(SteamHelperPageRead), "pageRead")]
[JsonDerivedType(typeof(SteamHelperCollections), "collections")]
[JsonDerivedType(typeof(SteamHelperFailed), "failed")]
[JsonDerivedType(typeof(SteamHelperFinished), "finished")]
internal abstract record SteamHelperEvent;

internal sealed record SteamHelperStarted : SteamHelperEvent;

internal sealed record SteamHelperProgress(ulong Id, ulong Bytes, ulong Total) : SteamHelperEvent;

internal sealed record SteamHelperItemDone(ulong Id) : SteamHelperEvent;

/// <param name="SteamResult">Steam's EResult; 0 (k_EResultNone) when Steam gave none.</param>
internal sealed record SteamHelperItemFailed(ulong Id, SteamHelperItemFailure Reason, int SteamResult) : SteamHelperEvent;

/// <summary>One page of a collection list was read; the list follows in <see cref="SteamHelperCollections"/>.</summary>
internal sealed record SteamHelperPageRead(SteamUserUgcList List, uint Page) : SteamHelperEvent;

/// <param name="AccountId">The account Steam answered for: the low 32 bits of its SteamID64.</param>
internal sealed record SteamHelperCollections(
    uint AccountId, IReadOnlyList<SteamHelperCollection> Favorited, IReadOnlyList<SteamHelperCollection> Published) : SteamHelperEvent;

/// <param name="Title"><c>null</c> when Steam returned none.</param>
internal sealed record SteamHelperCollection(ulong Id, string? Title, uint ChildCount);

internal sealed record SteamHelperFailed(SteamHelperFailure Reason, string Message) : SteamHelperEvent;

/// <summary>The last line of a run without <c>failed</c>, written after Steam shut down.</summary>
internal sealed record SteamHelperFinished : SteamHelperEvent;

// Wire format between two copies of the same launcher build, not part of CoreJsonContext's contract.
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(SteamHelperRequest))]
[JsonSerializable(typeof(SteamHelperEvent))]
internal sealed partial class SteamHelperJsonContext : JsonSerializerContext;
