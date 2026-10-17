namespace PzLauncher.Core.Common;

/// <summary>Throws <see cref="IOException"/> or <see cref="UnauthorizedAccessException"/>.</summary>
internal static class AtomicFile
{
    // Same folder, so the move is a rename: a reader never sees a half-written file.
    /// <summary>Without <paramref name="overwrite"/>, an existing file at <paramref name="path"/> makes the move throw and is kept.</summary>
    public static async Task WriteAsync(string path, byte[] content, CancellationToken ct, bool overwrite = true)
    {
        var temp = $"{path}.{Guid.NewGuid():N}.tmp";
        var moved = false;
        try
        {
            await File.WriteAllBytesAsync(temp, content, ct).ConfigureAwait(false);
            File.Move(temp, path, overwrite);
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
