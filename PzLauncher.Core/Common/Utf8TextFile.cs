using System.Text;

namespace PzLauncher.Core.Common;

internal readonly record struct Utf8Text(string Text, bool HasBom);

/// <summary>The bytes of a UTF-8 file we rewrite in place: strict decoding, and the BOM kept as found.</summary>
internal static class Utf8TextFile
{
    private static readonly byte[] Bom = [0xEF, 0xBB, 0xBF];

    public static Encoding StrictUtf8 { get; } = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>Invalid UTF-8 fails with <paramref name="malformed"/>, given the decoder's reason.</summary>
    public static Result<Utf8Text> Decode(byte[] bytes, Func<string, Error> malformed) => Decode(bytes, StrictUtf8, malformed);

    /// <summary>The BOM is found by its bytes; <paramref name="encoding"/> decodes the rest.</summary>
    public static Result<Utf8Text> Decode(byte[] bytes, Encoding encoding, Func<string, Error> malformed)
    {
        var hasBom = bytes.AsSpan().StartsWith(Bom);
        try
        {
            return new Utf8Text(hasBom ? encoding.GetString(bytes, Bom.Length, bytes.Length - Bom.Length) : encoding.GetString(bytes), hasBom);
        }
        catch (DecoderFallbackException ex)
        {
            // Replacement characters would change the bytes an edit doesn't touch.
            return malformed(ex.Message);
        }
    }

    public static byte[] Encode(string text, bool hasBom) => Encode(text, hasBom, StrictUtf8);

    public static byte[] Encode(string text, bool hasBom, Encoding encoding)
    {
        var body = encoding.GetBytes(text);
        return hasBom ? [.. Bom, .. body] : body;
    }
}
