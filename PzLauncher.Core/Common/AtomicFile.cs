namespace PzLauncher.Core.Common;

/// <summary>Throws <see cref="IOException"/> or <see cref="UnauthorizedAccessException"/>.</summary>
internal static class AtomicFile
{
    /// <summary>The rolling copy of a file we don't own, taken before each rewrite, for manual recovery.</summary>
    public const string BackupSuffix = ".pzlauncher.bak";

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

    /// <summary>Keeps <paramref name="original"/>, the file's current bytes, as the rolling backup before writing <paramref name="content"/>.</summary>
    public static async Task ReplaceWithBackupAsync(string path, byte[] original, byte[] content, CancellationToken ct)
    {
        await WriteAsync(path + BackupSuffix, original, ct).ConfigureAwait(false);
        await WriteAsync(path, content, ct).ConfigureAwait(false);
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
