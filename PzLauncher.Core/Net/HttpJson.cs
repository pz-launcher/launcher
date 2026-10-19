using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using PzLauncher.Core.Common;

namespace PzLauncher.Core.Net;

internal static class HttpJson
{
    public static async Task<Result<T>> SendAsync<T>(
        HttpClient http,
        HttpRequestMessage request,
        JsonTypeInfo<T> type,
        TimeSpan timeout,
        Func<string, Error> networkFailed,
        Func<string, Error> invalid,
        CancellationToken ct)
        where T : notnull
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);
        try
        {
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return networkFailed(string.Create(CultureInfo.InvariantCulture, $"HTTP {(int)response.StatusCode}"));
            }

            var body = await response.Content.ReadAsStreamAsync(timeoutCts.Token).ConfigureAwait(false);
            await using var _ = body.ConfigureAwait(false);
            var json = await JsonSerializer.DeserializeAsync(body, type, timeoutCts.Token).ConfigureAwait(false);
            return json is null ? invalid("empty body") : json;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return networkFailed("timed out");
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            return networkFailed(ex.Message);
        }
        catch (JsonException ex)
        {
            return invalid(ex.Message);
        }
    }
}
