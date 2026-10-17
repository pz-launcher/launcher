using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using PzLauncher.Core.Common;

namespace PzLauncher.Core.Update;

/// <param name="Release"><c>null</c>: the latest release is a prerelease or has no package for the platform.</param>
internal sealed record LatestRelease(ReleaseInfo? Release);

/// <summary>Reads one source's latest release; GitHub and Gitee share the response shape.</summary>
internal sealed class ReleaseFeed(ReleaseFeedEndpoint endpoint, HttpClient http)
{
    public UpdateSource Source => endpoint.Source;

    public async Task<Result<LatestRelease>> GetLatestAsync(string rid, TimeSpan timeout, CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint.LatestReleaseUrl);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return UpdateErrors.NetworkFailed(Source, string.Create(CultureInfo.InvariantCulture, $"HTTP {(int)response.StatusCode}"));
            }

            var body = await response.Content.ReadAsStreamAsync(timeoutCts.Token).ConfigureAwait(false);
            await using var _ = body.ConfigureAwait(false);
            var json = await JsonSerializer.DeserializeAsync(body, UpdateJsonContext.Default.ReleaseJson, timeoutCts.Token).ConfigureAwait(false);
            return json is null ? UpdateErrors.ReleaseInfoInvalid(Source, "empty body") : ToLatest(json, rid);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return UpdateErrors.NetworkFailed(Source, "timed out");
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            return UpdateErrors.NetworkFailed(Source, ex.Message);
        }
        catch (JsonException ex)
        {
            return UpdateErrors.ReleaseInfoInvalid(Source, ex.Message);
        }
    }

    private Result<LatestRelease> ToLatest(ReleaseJson json, string rid)
    {
        var tag = json.TagName;
        // D5: a '-' anywhere in the tag marks a prerelease, as in release.yml.
        if (json.Prerelease || tag.Contains('-', StringComparison.Ordinal))
        {
            return new LatestRelease(null);
        }

        if (!SemVersion.TryParse(tag.StartsWith('v') ? tag[1..] : tag, out var version))
        {
            return UpdateErrors.ReleaseInfoInvalid(Source, $"tag {tag} is not a version");
        }

        var packageName = LauncherReleases.PackageName(version, rid);
        var asset = json.Assets.FirstOrDefault(a => string.Equals(a.Name, packageName, StringComparison.Ordinal));
        if (asset is null)
        {
            return new LatestRelease(null);
        }

        // Plain HTTP would let anyone on the path swap the package; Gitee has no checksum to catch it.
        if (!Uri.TryCreate(asset.BrowserDownloadUrl, UriKind.Absolute, out var packageUrl) || packageUrl.Scheme != Uri.UriSchemeHttps)
        {
            return UpdateErrors.ReleaseInfoInvalid(Source, $"package URL {asset.BrowserDownloadUrl} is not HTTPS");
        }

        var sha256 = ParseDigest(asset.Digest);
        if (!sha256.IsSuccess)
        {
            return sha256.Error;
        }

        var releasePage = new Uri(endpoint.ReleasePagePrefix + Uri.EscapeDataString(tag));
        return new LatestRelease(new ReleaseInfo(version, Source, packageName, packageUrl, asset.Size, sha256.Value.Hex, releasePage));
    }

    // Another algorithm is ignored (no checksum); a malformed SHA-256 is an error, never a skipped check.
    private Result<Sha256Digest> ParseDigest(string? digest)
    {
        if (digest is null || !digest.StartsWith(LauncherReleases.Sha256DigestPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return new Sha256Digest(null);
        }

        var hex = digest[LauncherReleases.Sha256DigestPrefix.Length..];
        return hex.Length == 64 && hex.All(char.IsAsciiHexDigit)
            ? new Sha256Digest(hex.ToLowerInvariant())
            : UpdateErrors.ReleaseInfoInvalid(Source, $"digest {digest} is not a SHA-256");
    }

    private sealed record Sha256Digest(string? Hex);
}
