using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text;

namespace PzLauncher.Core.Server;

/// <summary>
/// Follows <c>server-console.txt</c> by polling: portable, and testable with a <see cref="TimeProvider"/>. PZ truncates the
/// file on every start and at its size limit, which shows up as the file being shorter than what was read.
/// </summary>
internal sealed class ServerConsoleTail(string path)
{
    private const int BacklogBytes = 256 * 1024;

    // A "line" this long without a newline is emitted as it is, so a broken file can't grow the buffer without bound.
    private const int MaxLineBytes = 64 * 1024;

    private const int ChunkBytes = 64 * 1024;

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    private readonly ArrayBufferWriter<byte> _pending = new();
    private long _offset;

    /// <summary>The backlog first, then new lines as they are written, until <paramref name="ct"/> is cancelled.</summary>
    public static async IAsyncEnumerable<ServerConsoleLine> FollowAsync(
        string path, int backlogLines, TimeProvider time, [EnumeratorCancellation] CancellationToken ct)
    {
        var tail = new ServerConsoleTail(path);
        foreach (var line in await tail.ReadBacklogAsync(backlogLines, ct).ConfigureAwait(false))
        {
            yield return new ServerConsoleLine(line, ServerConsoleLineKind.Output);
        }

        while (true)
        {
            await Task.Delay(PollInterval, time, ct).ConfigureAwait(false);
            foreach (var line in await tail.ReadNewAsync(ct).ConfigureAwait(false))
            {
                yield return line;
            }
        }
    }

    /// <summary>The last <paramref name="count"/> complete lines within the file's last 256 KB; later reads continue after them.</summary>
    public async Task<IReadOnlyList<string>> ReadBacklogAsync(int count, CancellationToken ct)
    {
        _pending.ResetWrittenCount();
        _offset = 0;
        byte[] window;
        long start;
        try
        {
            using var stream = Open();
            start = Math.Max(0, stream.Length - BacklogBytes);
            window = new byte[stream.Length - start];
            stream.Position = start;
            await stream.ReadExactlyAsync(window, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Missing (no server started in this folder yet) or unreadable: the next reads start from the beginning.
            return [];
        }

        var lastNewLine = Array.LastIndexOf(window, (byte)'\n');
        if (lastNewLine < 0)
        {
            _offset = start;
            return [];
        }

        _offset = start + lastNewLine + 1;

        // A window that doesn't start the file begins inside a line.
        var firstLine = start > 0 ? Array.IndexOf(window, (byte)'\n') + 1 : 0;
        var lines = SplitLines(window.AsSpan(firstLine, lastNewLine + 1 - firstLine));
        return count <= 0 ? [] : lines[Math.Max(0, lines.Count - count)..];
    }

    /// <summary>Complete lines written since the last read; a partial last line waits for its newline.</summary>
    public async Task<IReadOnlyList<ServerConsoleLine>> ReadNewAsync(CancellationToken ct)
    {
        var lines = new List<ServerConsoleLine>();
        try
        {
            using var stream = Open();
            if (stream.Length < _offset)
            {
                lines.Add(new ServerConsoleLine("", ServerConsoleLineKind.FileRestarted));
                _offset = 0;
                _pending.ResetWrittenCount();
            }

            stream.Position = _offset;
            var chunk = new byte[ChunkBytes];
            int read;
            while ((read = await stream.ReadAsync(chunk, ct).ConfigureAwait(false)) > 0)
            {
                _offset += read;
                Collect(chunk.AsSpan(0, read), lines);
            }
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            // Deleted: whatever is written next starts over.
            if (_offset > 0)
            {
                lines.Add(new ServerConsoleLine("", ServerConsoleLineKind.FileRestarted));
                _offset = 0;
                _pending.ResetWrittenCount();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Momentarily locked or unreadable: the next poll tries again from the same offset.
        }

        return lines;
    }

    // ReadWrite | Delete: PZ keeps the file open for writing and re-creates it on every start.
    private FileStream Open() => new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, bufferSize: 1, useAsync: true);

    private void Collect(ReadOnlySpan<byte> chunk, List<ServerConsoleLine> lines)
    {
        while (chunk.Length > 0)
        {
            var newLine = chunk.IndexOf((byte)'\n');
            if (newLine < 0)
            {
                _pending.Write(chunk);
                if (_pending.WrittenCount >= MaxLineBytes)
                {
                    lines.Add(new ServerConsoleLine(Decode(_pending.WrittenSpan), ServerConsoleLineKind.Output));
                    _pending.ResetWrittenCount();
                }

                return;
            }

            _pending.Write(chunk[..newLine]);
            lines.Add(new ServerConsoleLine(Decode(_pending.WrittenSpan), ServerConsoleLineKind.Output));
            _pending.ResetWrittenCount();
            chunk = chunk[(newLine + 1)..];
        }
    }

    // Every element ends with '\n'.
    private static List<string> SplitLines(ReadOnlySpan<byte> text)
    {
        var lines = new List<string>();
        while (text.Length > 0)
        {
            var newLine = text.IndexOf((byte)'\n');
            lines.Add(Decode(text[..newLine]));
            text = text[(newLine + 1)..];
        }

        return lines;
    }

    // Lossy on purpose: the console is only shown, and one bad byte mustn't hide a line.
    private static string Decode(ReadOnlySpan<byte> line) =>
        Encoding.UTF8.GetString(line.Length > 0 && line[^1] == (byte)'\r' ? line[..^1] : line);
}
