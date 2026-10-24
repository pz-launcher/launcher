using System.Text.Json.Serialization;

namespace PzLauncher.Core.Mod.Collections.Files;

internal sealed record ModCollectionOrderDocument(IReadOnlyDictionary<string, IReadOnlyList<string>> Collections);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(ModCollectionOrderDocument))]
internal sealed partial class ModCollectionOrderJsonContext : JsonSerializerContext;
