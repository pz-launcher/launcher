using System.Text;

namespace PzLauncher.Core.Common;

internal readonly record struct TextLine(string Content, string Terminator);

/// <summary>The feature's errors for a <see cref="GameTextFile"/>, each given the path and a reason.</summary>
internal sealed record GameTextFileErrors(
    Func<string, string, Error> Unreadable,
    Func<string, string, Error> Malformed,
    Func<string, string, Error> WriteFailed);

/// <summary>
/// A game text file as lines that keep their own terminators (and the file its BOM), so a rewrite changes only
/// the lines an edit touched.
/// </summary>
internal sealed class GameTextFile
{
    private readonly byte[]? _original;
    private readonly bool _hasBom;
    private readonly Encoding _encoding;
    private readonly GameTextFileErrors _errors;

    private GameTextFile(
        string path, byte[]? original, bool hasBom, List<TextLine> lines, string newline, Encoding encoding, GameTextFileErrors errors)
    {
        FilePath = path;
        _original = original;
        _hasBom = hasBom;
        Lines = lines;
        Newline = newline;
        _encoding = encoding;
        _errors = errors;
    }

    public string FilePath { get; }

    public bool Exists => _original is not null;

    public List<TextLine> Lines { get; }

    /// <summary>The file's first line terminator; new lines get it.</summary>
    public string Newline { get; }

    /// <summary>Strict UTF-8; a missing file reads as empty, with <paramref name="newline"/> for the lines added to it.</summary>
    public static Task<Result<GameTextFile>> ReadAsync(string path, string newline, GameTextFileErrors errors, CancellationToken ct) =>
        ReadAsync(path, newline, Utf8TextFile.StrictUtf8, errors, ct);

    /// <summary><paramref name="encoding"/> decodes and encodes the text after the BOM.</summary>
    public static async Task<Result<GameTextFile>> ReadAsync(
        string path, string newline, Encoding encoding, GameTextFileErrors errors, CancellationToken ct)
    {
        var read = await ReadFile.BytesIfExistsAsync(path, reason => errors.Unreadable(path, reason), ct).ConfigureAwait(false);
        if (read is not { } result)
        {
            return new GameTextFile(path, null, false, [], newline, encoding, errors);
        }

        if (!result.IsSuccess)
        {
            return result.Error;
        }

        var bytes = result.Value;
        var decoded = Utf8TextFile.Decode(bytes, encoding, reason => errors.Malformed(path, reason));
        if (!decoded.IsSuccess)
        {
            return decoded.Error;
        }

        var lines = Split(decoded.Value.Text);
        var own = lines.Select(line => line.Terminator).FirstOrDefault(terminator => terminator.Length > 0);
        return new GameTextFile(path, bytes, decoded.Value.HasBom, lines, own ?? newline, encoding, errors);
    }

    public void Add(string content) => Insert(Lines.Count, content);

    /// <summary>A line inserted after an unterminated last line terminates it first.</summary>
    public void Insert(int index, string content)
    {
        if (index == Lines.Count && Lines.Count > 0 && Lines[^1].Terminator.Length == 0)
        {
            Lines[^1] = Lines[^1] with { Terminator = Newline };
        }

        Lines.Insert(index, new TextLine(content, Newline));
    }

    /// <summary>
    /// Writes nothing when unchanged. Before replacing an existing file, keeps it as the rolling <c>.pzlauncher.bak</c>
    /// when <paramref name="rollingBackup"/>, and as <paramref name="originalCopy"/> when that is given and doesn't exist yet.
    /// </summary>
    public async Task<Result> WriteAsync(bool rollingBackup, string? originalCopy, CancellationToken ct)
    {
        var content = Encode();
        if (_original is not null && content.AsSpan().SequenceEqual(_original))
        {
            return Result.Ok();
        }

        try
        {
            if (Path.GetDirectoryName(FilePath) is { } folder)
            {
                Directory.CreateDirectory(folder);
            }

            if (_original is null)
            {
                await AtomicFile.WriteAsync(FilePath, content, ct).ConfigureAwait(false);
                return Result.Ok();
            }

            if (originalCopy is not null && !File.Exists(originalCopy))
            {
                await AtomicFile.WriteAsync(originalCopy, _original, ct, overwrite: false).ConfigureAwait(false);
            }

            if (rollingBackup)
            {
                await AtomicFile.ReplaceWithBackupAsync(FilePath, _original, content, ct).ConfigureAwait(false);
            }
            else
            {
                await AtomicFile.WriteAsync(FilePath, content, ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return _errors.WriteFailed(FilePath, ex.Message);
        }

        return Result.Ok();
    }

    private byte[] Encode()
    {
        var text = new StringBuilder();
        foreach (var line in Lines)
        {
            text.Append(line.Content).Append(line.Terminator);
        }

        return Utf8TextFile.Encode(text.ToString(), _hasBom, _encoding);
    }

    private static List<TextLine> Split(string text)
    {
        var lines = new List<TextLine>();
        var start = 0;
        while (start < text.Length)
        {
            var end = text.IndexOf('\n', start);
            if (end < 0)
            {
                lines.Add(new TextLine(text[start..], ""));
                break;
            }

            var crlf = end > start && text[end - 1] == '\r';
            lines.Add(new TextLine(text[start..(crlf ? end - 1 : end)], crlf ? "\r\n" : "\n"));
            start = end + 1;
        }

        return lines;
    }
}
