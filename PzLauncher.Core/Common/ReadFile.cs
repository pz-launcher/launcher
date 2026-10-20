namespace PzLauncher.Core.Common;

/// <summary>Reads a whole file we don't own; an I/O or access failure becomes the caller's error, given the exception's message.</summary>
internal static class ReadFile
{
    public static async Task<Result<string>> TextAsync(string path, Func<string, Error> unreadable, CancellationToken ct)
    {
        try
        {
            return await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return unreadable(ex.Message);
        }
    }

    /// <summary><c>null</c> when the file or its folder doesn't exist.</summary>
    public static Task<Result<string>?> TextIfExistsAsync(string path, Func<string, Error> unreadable, CancellationToken ct) =>
        IfExistsAsync(() => File.ReadAllTextAsync(path, ct), unreadable);

    /// <summary><c>null</c> when the file or its folder doesn't exist.</summary>
    public static Task<Result<byte[]>?> BytesIfExistsAsync(string path, Func<string, Error> unreadable, CancellationToken ct) =>
        IfExistsAsync(() => File.ReadAllBytesAsync(path, ct), unreadable);

    private static async Task<Result<T>?> IfExistsAsync<T>(Func<Task<T>> read, Func<string, Error> unreadable)
        where T : notnull
    {
        try
        {
            return await read().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return unreadable(ex.Message);
        }
    }
}
