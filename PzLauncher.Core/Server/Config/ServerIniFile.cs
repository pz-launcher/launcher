using System.Text;
using PzLauncher.Core.Common;

namespace PzLauncher.Core.Server.Config;

/// <summary>
/// A server's <c>&lt;name&gt;.ini</c> as bytes: <see cref="Set"/> rewrites only that key's lines, so every other line keeps
/// its bytes whatever their encoding.
/// </summary>
internal sealed class ServerIniFile
{
    private const byte NewLine = (byte)'\n';
    private const byte CarriageReturn = (byte)'\r';

    private ServerIniFile(string path, byte[] content)
    {
        Path = path;
        Content = content;
    }

    public string Path { get; }

    public byte[] Content { get; }

    /// <summary><c>null</c> when the file doesn't exist.</summary>
    public static async Task<Result<ServerIniFile>?> ReadAsync(string path, CancellationToken ct)
    {
        var bytes = await ReadFile.BytesIfExistsAsync(path, reason => ServerErrors.FileAccessFailed(path, reason), ct).ConfigureAwait(false);
        return bytes switch
        {
            null => null,
            { IsSuccess: true } read => new ServerIniFile(path, read.Value),
            { } failed => failed.Error,
        };
    }

    public static byte[] Create(IReadOnlyList<KeyValuePair<string, string>> entries) =>
        Encoding.UTF8.GetBytes(string.Concat(entries.Select(entry => $"{entry.Key}={entry.Value}\n")));

    /// <summary>The value of the key's last line, as PZ reads it; <c>null</c> when absent.</summary>
    public string? Get(string key)
    {
        string? value = null;
        foreach (var (start, length) in Lines())
        {
            if (Parse(start, length) is { } entry && string.Equals(entry.Key, key, StringComparison.Ordinal))
            {
                value = entry.Value;
            }
        }

        return value;
    }

    /// <summary>Replaces every line of the key, or appends one in the file's line-ending style.</summary>
    public ServerIniFile Set(string key, string value)
    {
        var line = Encoding.UTF8.GetBytes($"{key}={value}");
        using var output = new MemoryStream();
        var copied = 0;
        var found = false;
        foreach (var (start, length) in Lines())
        {
            if (Parse(start, length) is { } entry && string.Equals(entry.Key, key, StringComparison.Ordinal))
            {
                output.Write(Content, copied, start - copied);
                output.Write(line);
                copied = start + length;
                found = true;
            }
        }

        output.Write(Content, copied, Content.Length - copied);
        if (!found)
        {
            ReadOnlySpan<byte> newLine = Content.AsSpan().IndexOf("\r\n"u8) >= 0 ? "\r\n"u8 : "\n"u8;
            if (Content.Length > 0 && Content[^1] != NewLine)
            {
                output.Write(newLine);
            }

            output.Write(line);
            output.Write(newLine);
        }

        return new ServerIniFile(Path, output.ToArray());
    }

    // Each line's start and length, without its "\n" or "\r\n".
    private List<(int Start, int Length)> Lines()
    {
        var lines = new List<(int, int)>();
        var start = 0;
        while (start < Content.Length)
        {
            var end = Array.IndexOf(Content, NewLine, start);
            var next = end < 0 ? Content.Length : end + 1;
            var contentEnd = end < 0 ? Content.Length : end;
            if (contentEnd > start && Content[contentEnd - 1] == CarriageReturn)
            {
                contentEnd--;
            }

            lines.Add((start, contentEnd - start));
            start = next;
        }

        return lines;
    }

    private KeyValuePair<string, string>? Parse(int start, int length)
    {
        var text = Encoding.UTF8.GetString(Content, start, length).Trim();
        var equals = text.IndexOf('=', StringComparison.Ordinal);
        return text.StartsWith('#') || equals <= 0
            ? null
            : new KeyValuePair<string, string>(text[..equals].Trim(), text[(equals + 1)..].Trim());
    }
}
