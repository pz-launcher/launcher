using System.Text.Json.Serialization;

namespace PzLauncher.Core.Steam.Workshop;

/// <summary>The subset of <c>GetPublishedFileDetails</c>' body the launcher reads.</summary>
internal sealed record PublishedFileDetailsJson
{
    [JsonPropertyName("response")]
    public PublishedFileDetailsResponseJson? Response { get; init; }
}

internal sealed record PublishedFileDetailsResponseJson
{
    [JsonPropertyName("publishedfiledetails")]
    public IReadOnlyList<PublishedFileJson>? Files { get; init; }
}

internal sealed record PublishedFileJson
{
    [JsonPropertyName("publishedfileid")]
    public ulong? Id { get; init; }

    [JsonPropertyName("result")]
    public int? Result { get; init; }

    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("hcontent_file")]
    public string? ContentManifest { get; init; }

    [JsonPropertyName("time_updated")]
    public long? TimeUpdated { get; init; }

    [JsonPropertyName("file_size")]
    public long? FileSize { get; init; }
}

/// <summary>The subset of <c>GetCollectionDetails</c>' body the launcher reads.</summary>
internal sealed record CollectionDetailsJson
{
    [JsonPropertyName("response")]
    public CollectionDetailsResponseJson? Response { get; init; }
}

internal sealed record CollectionDetailsResponseJson
{
    [JsonPropertyName("collectiondetails")]
    public IReadOnlyList<CollectionJson>? Collections { get; init; }
}

internal sealed record CollectionJson
{
    [JsonPropertyName("publishedfileid")]
    public ulong? Id { get; init; }

    [JsonPropertyName("result")]
    public int? Result { get; init; }

    [JsonPropertyName("children")]
    public IReadOnlyList<CollectionChildJson>? Children { get; init; }
}

internal sealed record CollectionChildJson
{
    [JsonPropertyName("publishedfileid")]
    public ulong? Id { get; init; }

    [JsonPropertyName("sortorder")]
    public int? SortOrder { get; init; }

    [JsonPropertyName("filetype")]
    public int? FileType { get; init; }
}

// External format, so not part of CoreJsonContext's contract. Ids and sizes arrive as JSON strings.
[JsonSourceGenerationOptions(
    NumberHandling = JsonNumberHandling.AllowReadingFromString,
    RespectNullableAnnotations = true)]
[JsonSerializable(typeof(PublishedFileDetailsJson))]
[JsonSerializable(typeof(CollectionDetailsJson))]
internal sealed partial class WorkshopWebApiJsonContext : JsonSerializerContext;
