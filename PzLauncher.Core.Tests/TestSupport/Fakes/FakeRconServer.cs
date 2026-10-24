using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace PzLauncher.Core.Tests.TestSupport.Fakes;

/// <summary>A loopback RCON listener that behaves like PZ's (task research/pz-server-runtime.md); one connection at a time.</summary>
internal sealed class FakeRconServer : IAsyncDisposable
{
    private const int MaxBodyBytes = 4086;

    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly string _password;
    private readonly Func<string, string> _respond;
    private readonly Task _loop;

    public FakeRconServer(string password, Func<string, string> respond)
    {
        _password = password;
        _respond = respond;
        _listener.Start();
        _loop = Task.Run(AcceptLoopAsync);
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public List<string> Commands { get; } = [];

    /// <summary>Sends the first packet of a response, then closes the connection.</summary>
    public bool CloseMidResponse { get; init; }

    /// <summary>Accepts connections but never answers.</summary>
    public bool Silent { get; init; }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _listener.Stop();
        try
        {
            await _loop;
        }
        catch (OperationCanceledException)
        {
        }

        _stop.Dispose();
    }

    private async Task AcceptLoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_stop.Token);
            }
            catch (Exception ex) when (ex is SocketException or ObjectDisposedException or OperationCanceledException)
            {
                return;
            }

            using (client)
            {
                try
                {
                    await ServeAsync(client.GetStream(), _stop.Token);
                }
                catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException)
                {
                    // The client went away.
                }
            }
        }
    }

    private async Task ServeAsync(NetworkStream stream, CancellationToken ct)
    {
        while (true)
        {
            var (id, type, body) = await ReadAsync(stream, ct);
            if (Silent)
            {
                continue;
            }

            switch (type)
            {
                case 3:
                    await WriteAsync(stream, id, 0, [], ct);
                    var accepted = body == _password;
                    await WriteAsync(stream, accepted ? id : -1, 2, [], ct);
                    if (!accepted)
                    {
                        return;
                    }

                    break;
                case 2:
                    Commands.Add(body);
                    var response = Encoding.UTF8.GetBytes(_respond(body));
                    for (var offset = 0; offset == 0 || offset < response.Length; offset += MaxBodyBytes)
                    {
                        await WriteAsync(stream, id, 0, response.AsSpan(offset, Math.Min(MaxBodyBytes, response.Length - offset)).ToArray(), ct);
                        if (CloseMidResponse)
                        {
                            return;
                        }
                    }

                    break;
                default:
                    await WriteAsync(stream, id, 0, [], ct);
                    break;
            }
        }
    }

    private static async Task<(int Id, int Type, string Body)> ReadAsync(NetworkStream stream, CancellationToken ct)
    {
        var header = new byte[4];
        await stream.ReadExactlyAsync(header, ct);
        var data = new byte[BinaryPrimitives.ReadInt32LittleEndian(header)];
        await stream.ReadExactlyAsync(data, ct);
        return (BinaryPrimitives.ReadInt32LittleEndian(data), BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(4)), Encoding.UTF8.GetString(data, 8, data.Length - 10));
    }

    private static async Task WriteAsync(NetworkStream stream, int id, int type, byte[] body, CancellationToken ct)
    {
        var packet = new byte[14 + body.Length];
        BinaryPrimitives.WriteInt32LittleEndian(packet, packet.Length - 4);
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(4), id);
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(8), type);
        body.CopyTo(packet.AsSpan(12));
        await stream.WriteAsync(packet, ct);
    }
}
