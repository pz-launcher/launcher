using System.Text.Json.Serialization;

namespace PzLauncher.Core.Update;

/// <summary>The subset of GitHub's and Gitee's <c>releases/latest</c> body the feed reads; both share these names.</summary>
internal sealed record ReleaseJson(string TagName, bool Prerelease, IReadOnlyList<ReleaseAssetJson> Assets);

/// <param name="Size">GitHub only.</param>
/// <param name="Digest">GitHub only, e.g. <c>sha256:&lt;hex&gt;</c>.</param>
internal sealed record ReleaseAssetJson(string Name, string BrowserDownloadUrl, long? Size = null, string? Digest = null);

/// <summary>Content of <c>staged/&lt;version&gt;.ready</c>.</summary>
internal sealed record StagedMarkerJson(string ReleasePageUrl);

// External formats, so not part of CoreJsonContext's contract.
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(ReleaseJson))]
[JsonSerializable(typeof(StagedMarkerJson))]
internal sealed partial class UpdateJsonContext : JsonSerializerContext;
