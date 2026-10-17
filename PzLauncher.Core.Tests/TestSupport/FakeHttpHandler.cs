using System.Net;
using System.Text;

namespace PzLauncher.Core.Tests.TestSupport;

/// <summary>Answers by absolute URL; an unknown URL is a 404. Never touches the network.</summary>
public sealed class FakeHttpHandler : HttpMessageHandler
{
    private readonly Dictionary<string, Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>> _routes = new(StringComparer.Ordinal);
    private readonly List<RecordedRequest> _requests = [];
    private readonly List<Uri> _cancelled = [];

    public IReadOnlyList<RecordedRequest> Requests
    {
        get
        {
            lock (_requests)
            {
                return [.. _requests];
            }
        }
    }

    public IReadOnlyList<Uri> Cancelled
    {
        get
        {
            lock (_cancelled)
            {
                return [.. _cancelled];
            }
        }
    }

    public IReadOnlyList<string> RequestedHosts => [.. Requests.Select(request => request.Uri.Host)];

    public void Respond(string url, Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) => _routes[url] = respond;

    public void RespondJson(string url, string json) =>
        Respond(url, (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") }));

    public void RespondBytes(string url, byte[] content) =>
        Respond(url, (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(content) }));

    public void RespondStream(string url, Func<Stream> content) =>
        Respond(url, (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(content()) }));

    public void RespondStatus(string url, HttpStatusCode status) =>
        Respond(url, (_, _) => Task.FromResult(new HttpResponseMessage(status)));

    public void Throw(string url) =>
        Respond(url, (_, _) => throw new HttpRequestException("Connection refused"));

    /// <summary>Never answers until the request is cancelled.</summary>
    public void Hang(string url) =>
        Respond(url, async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            throw new InvalidOperationException("unreachable");
        });

    /// <summary>Answers once <paramref name="release"/> completes.</summary>
    public void RespondJsonAfter(string url, string json, Task release) =>
        Respond(url, async (_, ct) =>
        {
            await release.WaitAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        });

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        lock (_requests)
        {
            _requests.Add(new RecordedRequest(request.RequestUri!, request.Headers.UserAgent.ToString(), request.Headers.Accept.ToString()));
        }

        var url = request.RequestUri?.AbsoluteUri ?? "";
        if (!_routes.TryGetValue(url, out var respond))
        {
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        try
        {
            return await respond(request, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            lock (_cancelled)
            {
                _cancelled.Add(request.RequestUri!);
            }

            throw;
        }
    }
}

public sealed record RecordedRequest(Uri Uri, string UserAgent, string Accept);

/// <summary>Returns <see cref="Prefix"/>, then blocks every read until cancelled.</summary>
public sealed class StallingStream(byte[] prefix) : Stream
{
    private int _position;

    public byte[] Prefix => prefix;

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_position < prefix.Length)
        {
            var count = Math.Min(buffer.Length, prefix.Length - _position);
            prefix.AsMemory(_position, count).CopyTo(buffer);
            _position += count;
            return count;
        }

        await Task.Delay(Timeout.Infinite, cancellationToken);
        return 0;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
