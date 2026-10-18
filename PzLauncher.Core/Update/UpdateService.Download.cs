using System.Formats.Tar;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using PzLauncher.Core.Common;
using PzLauncher.Core.Platform;

namespace PzLauncher.Core.Update;

internal sealed partial class UpdateService
{
    private const int BufferSize = 81920;

    public async Task<Result<StagedUpdate>> DownloadAsync(
        ReleaseInfo release, IProgress<DownloadProgress>? progress = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (current.IsDevelopment)
        {
            return Logged(UpdateErrors.DevelopmentBuild(current));
        }

        // The name becomes a file name below .update; it must not climb out of it.
        if (!string.Equals(Path.GetFileName(release.PackageName), release.PackageName, StringComparison.Ordinal)
            || PathNormalizer.HasInvalidPathChars(release.PackageName)
            || release.PackageName is "" or "." or "..")
        {
            return Logged(UpdateErrors.PackageInvalid(release.PackageName, "not a file name"));
        }

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var paths = Paths;
            if (File.Exists(paths.ReadyMarker(release.Version)) && Directory.Exists(paths.StagedDirectory(release.Version)))
            {
                var existing = await ReadStagedAsync(paths, release.Version, ct).ConfigureAwait(false);
                if (existing is not null)
                {
                    return existing;
                }
            }

            try
            {
                Directory.CreateDirectory(paths.Download);
                Directory.CreateDirectory(paths.Staged);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return Logged(UpdateErrors.InstallFolderNotWritable(InstallDirectory, ex.Message));
            }

            var package = Path.Combine(paths.Download, release.PackageName);
            var downloaded = await DownloadPackageAsync(release, package, progress, ct).ConfigureAwait(false);
            if (!downloaded.IsSuccess)
            {
                return Logged(downloaded.Error);
            }

            try
            {
                var staged = await StageAsync(paths, release, package, ct).ConfigureAwait(false);
                return staged.IsSuccess ? staged : Logged(staged.Error);
            }
            finally
            {
                TryDeleteFile(package);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<Result> DownloadPackageAsync(
        ReleaseInfo release, string package, IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        var part = package + LauncherReleases.PartialSuffix;
        var completed = false;
        using var stall = CancellationTokenSource.CreateLinkedTokenSource(ct);
        try
        {
            stall.CancelAfter(StallTimeout);
            using var response = await http.Client.GetAsync(release.PackageUrl, HttpCompletionOption.ResponseHeadersRead, stall.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return UpdateErrors.NetworkFailed(release.Source, string.Create(CultureInfo.InvariantCulture, $"HTTP {(int)response.StatusCode}"));
            }

            var total = release.Size ?? response.Content.Headers.ContentLength;
            var input = await response.Content.ReadAsStreamAsync(stall.Token).ConfigureAwait(false);
            await using var inputScope = input.ConfigureAwait(false);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var received = 0L;
            var output = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, FileOptions.Asynchronous);
            await using (output.ConfigureAwait(false))
            {
                var buffer = new byte[BufferSize];
                while (true)
                {
                    stall.CancelAfter(StallTimeout);
                    int read;
                    try
                    {
                        read = await input.ReadAsync(buffer, stall.Token).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is IOException or HttpRequestException)
                    {
                        return UpdateErrors.NetworkFailed(release.Source, ex.Message);
                    }

                    if (read == 0)
                    {
                        break;
                    }

                    received += read;
                    if (received > release.Size)
                    {
                        return UpdateErrors.PackageInvalid(release.PackageName, "larger than the published size");
                    }

                    hash.AppendData(buffer, 0, read);
                    await output.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                    progress?.Report(new DownloadProgress(received, total));
                }
            }

            if (release.Size is { } size && received != size)
            {
                return UpdateErrors.PackageInvalid(release.PackageName, "smaller than the published size");
            }

            if (release.Sha256 is { } expected
                && !string.Equals(Convert.ToHexStringLower(hash.GetHashAndReset()), expected, StringComparison.OrdinalIgnoreCase))
            {
                return UpdateErrors.PackageInvalid(release.PackageName, "SHA-256 mismatch");
            }

            File.Move(part, package, overwrite: true);
            completed = true;
            return Result.Ok();
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return UpdateErrors.NetworkFailed(release.Source, "timed out");
        }
        catch (HttpRequestException ex)
        {
            return UpdateErrors.NetworkFailed(release.Source, ex.Message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return UpdateErrors.InstallFolderNotWritable(InstallDirectory, ex.Message);
        }
        finally
        {
            if (!completed)
            {
                TryDeleteFile(part);
            }
        }
    }

    private async Task<Result<StagedUpdate>> StageAsync(UpdatePaths paths, ReleaseInfo release, string package, CancellationToken ct)
    {
        var version = release.Version;
        var dir = paths.StagedDirectory(version);
        var marker = paths.ReadyMarker(version);
        var completed = false;
        try
        {
            // Leftovers of an interrupted extraction of this version.
            TryDeleteFile(marker);
            TryDeleteDirectory(dir);
            Directory.CreateDirectory(dir);
            // Both extractors reject entries that would land outside dir (zip-slip) with IOException.
            if (LauncherReleases.IsZip(release.PackageName))
            {
                await ZipFile.ExtractToDirectoryAsync(package, dir, overwriteFiles: false, ct).ConfigureAwait(false);
            }
            else
            {
                var gzip = new GZipStream(
                    new FileStream(package, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.Asynchronous),
                    CompressionMode.Decompress);
                await using var gzipScope = gzip.ConfigureAwait(false);
                await TarFile.ExtractToDirectoryAsync(gzip, dir, overwriteFiles: false, ct).ConfigureAwait(false);
            }

            var content = paths.StagedContent(version);
            var executable = Path.Combine(content, LauncherReleases.ExecutableName(platform.Os));
            if (!File.Exists(executable))
            {
                return UpdateErrors.PackageInvalid(
                    release.PackageName, $"no {LauncherReleases.PackageFolder}/{Path.GetFileName(executable)} in the package");
            }

            if (platform.Os != PlatformKind.Windows)
            {
                HostRuntime.EnsureUserExecutable(executable);
            }

            var markerJson = JsonSerializer.SerializeToUtf8Bytes(new StagedMarkerJson(release.ReleasePageUrl.AbsoluteUri), UpdateJsonContext.Default.StagedMarkerJson);
            await AtomicFile.WriteAsync(marker, markerJson, ct).ConfigureAwait(false);
            completed = true;

            // One staged version at a time; the previous one is dropped only now, so a failed download keeps offering it.
            foreach (var entry in Directory.EnumerateFileSystemEntries(paths.Staged).Where(entry => entry != dir && entry != marker).ToList())
            {
                if (Directory.Exists(entry))
                {
                    TryDeleteDirectory(entry);
                }
                else
                {
                    TryDeleteFile(entry);
                }
            }

            return new StagedUpdate(version, content, release.ReleasePageUrl);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException)
        {
            return UpdateErrors.PackageInvalid(release.PackageName, ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            return UpdateErrors.InstallFolderNotWritable(InstallDirectory, ex.Message);
        }
        finally
        {
            if (!completed)
            {
                TryDeleteFile(marker);
                TryDeleteDirectory(dir);
            }
        }
    }
}
