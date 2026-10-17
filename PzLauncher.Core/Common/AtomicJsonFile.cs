using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PzLauncher.Core.Common;

/// <summary>Writes indented UTF-8 without a BOM; throws <see cref="IOException"/> or <see cref="UnauthorizedAccessException"/>.</summary>
internal static class AtomicJsonFile
{
    // The default encoder would write '+' in "-XX:+UseZGC" as +.
    private static readonly JsonWriterOptions WriteOptions = new()
    {
        Indented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static Task WriteAsync(string path, JsonNode root, CancellationToken ct) =>
        AtomicFile.WriteAsync(path, Serialize(root), ct);

    private static byte[] Serialize(JsonNode root)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, WriteOptions))
        {
            root.WriteTo(writer);
        }

        return stream.ToArray();
    }
}
