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

    // Same folder, so the move is a rename: a reader never sees a half-written file.
    public static async Task WriteAsync(string path, JsonNode root, CancellationToken ct)
    {
        var temp = $"{path}.{Guid.NewGuid():N}.tmp";
        var moved = false;
        try
        {
            await File.WriteAllBytesAsync(temp, Serialize(root), ct).ConfigureAwait(false);
            File.Move(temp, path, overwrite: true);
            moved = true;
        }
        finally
        {
            if (!moved)
            {
                TryDelete(temp);
            }
        }
    }

    private static byte[] Serialize(JsonNode root)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, WriteOptions))
        {
            root.WriteTo(writer);
        }

        return stream.ToArray();
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort: a stray temp file is harmless.
        }
    }
}
