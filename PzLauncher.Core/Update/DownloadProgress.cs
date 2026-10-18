namespace PzLauncher.Core.Update;

/// <param name="Total">Bytes; <c>null</c> when neither the source nor the server tells the size.</param>
public sealed record DownloadProgress(long Received, long? Total);
