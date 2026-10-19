using System.Text.Json.Serialization;
using PzLauncher.Core.Steam.Workshop.Actions;

namespace PzLauncher.Core.Steam.Workshop.Helper;

/// <summary>The one JSON line the helper reads from stdin.</summary>
internal sealed record SteamHelperRequest(WorkshopAction Action, string SteamApiPath, IReadOnlyList<ulong> Ids);

internal enum SteamHelperFailure
{
    BadRequest,
    LibraryNotLoaded,
    ExportMissing,
    SteamNotRunning,
    VersionMismatch,
    InitFailed,
}

/// <summary>One JSON line the helper writes to stdout.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "event")]
[JsonDerivedType(typeof(SteamHelperStarted), "started")]
[JsonDerivedType(typeof(SteamHelperProgress), "progress")]
[JsonDerivedType(typeof(SteamHelperItemDone), "itemDone")]
[JsonDerivedType(typeof(SteamHelperItemFailed), "itemFailed")]
[JsonDerivedType(typeof(SteamHelperFailed), "failed")]
[JsonDerivedType(typeof(SteamHelperFinished), "finished")]
internal abstract record SteamHelperEvent;

internal sealed record SteamHelperStarted : SteamHelperEvent;

internal sealed record SteamHelperProgress(ulong Id, ulong Bytes, ulong Total) : SteamHelperEvent;

internal sealed record SteamHelperItemDone(ulong Id) : SteamHelperEvent;

/// <param name="SteamResult">Steam's EResult; 0 (k_EResultNone) when Steam gave none.</param>
internal sealed record SteamHelperItemFailed(ulong Id, WorkshopItemFailure Reason, int SteamResult) : SteamHelperEvent;

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
