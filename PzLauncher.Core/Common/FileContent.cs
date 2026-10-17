using System.Security.Cryptography;

namespace PzLauncher.Core.Common;

/// <summary>Throws <see cref="IOException"/> or <see cref="UnauthorizedAccessException"/>.</summary>
internal static class FileContent
{
    public static async Task<bool> EqualAsync(string path, string other, CancellationToken ct)
    {
        var hash = await HashAsync(path, ct).ConfigureAwait(false);
        var otherHash = await HashAsync(other, ct).ConfigureAwait(false);
        return hash.AsSpan().SequenceEqual(otherHash);
    }

    private static async Task<byte[]> HashAsync(string path, CancellationToken ct)
    {
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 81920, useAsync: true);
        await using (stream.ConfigureAwait(false))
        {
            return await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false);
        }
    }
}
