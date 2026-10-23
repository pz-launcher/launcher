using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using PzLauncher.Core.Common;

namespace PzLauncher.Core.Server.Rcon;

// Packets: little-endian size, id, type, then the UTF-8 body and two NULs (task research/pz-server-runtime.md, RCON).
internal sealed class RconClient(TimeProvider time) : IRconClient
{
    private const int ResponseValueType = 0;
    private const int ExecCommandType = 2;
    private const int AuthResponseType = 2;
    private const int AuthType = 3;

    private const int AuthId = 1;
    private const int CommandId = 2;
    private const int SentinelId = 3;
    private const int RejectedId = -1;

    private const int MinPacketSize = 10;

    // PZ sends at most 4096 bytes per packet; far more is not RCON.
    private const int MaxPacketSize = 64 * 1024;

    internal TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);

    public async Task<Result> AuthenticateAsync(int port, string password, CancellationToken ct)
    {
        var result = await RunAsync(port, password, null, ct).ConfigureAwait(false);
        return result.IsSuccess ? Result.Ok() : result.Error;
    }

    public Task<Result<string>> ExecuteAsync(int port, string password, string command, CancellationToken ct) =>
        RunAsync(port, password, command, ct);

    private async Task<Result<string>> RunAsync(int port, string password, string? command, CancellationToken ct)
    {
        using var timeout = new CancellationTokenSource(Timeout, time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        var token = linked.Token;
        try
        {
            using var client = new TcpClient(AddressFamily.InterNetwork);
            await client.ConnectAsync(IPAddress.Loopback, port, token).ConfigureAwait(false);
            var stream = client.GetStream();
            await WriteAsync(stream, AuthId, AuthType, password, token).ConfigureAwait(false);
            if (!await ReadAuthResponseAsync(stream, token).ConfigureAwait(false))
            {
                return ServerErrors.RconFailed("The server rejected the RCON password.");
            }

            if (command is null)
            {
                return "";
            }

            // PZ answers in request order and echoes an empty response value, so the echo marks the end of the command's packets.
            await WriteAsync(stream, CommandId, ExecCommandType, command, token).ConfigureAwait(false);
            await WriteAsync(stream, SentinelId, ResponseValueType, "", token).ConfigureAwait(false);
            using var response = new MemoryStream();
            while (true)
            {
                var packet = await ReadAsync(stream, token).ConfigureAwait(false);
                if (packet.Id == SentinelId)
                {
                    break;
                }

                if (packet.Id == CommandId)
                {
                    response.Write(packet.Body);
                }
            }

            // Decoded whole: PZ splits long responses by byte count, which can cut a character in two.
            return Encoding.UTF8.GetString(response.GetBuffer(), 0, (int)response.Length);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            return ServerErrors.RconFailed($"No answer within {Timeout.TotalSeconds:0} seconds.");
        }
        catch (EndOfStreamException)
        {
            return ServerErrors.RconFailed("The server closed the connection.");
        }
        catch (Exception ex) when (ex is SocketException or IOException or InvalidDataException)
        {
            return ServerErrors.RconFailed(ex.Message);
        }
    }

    // An empty response value comes first, then the verdict: the request id, or -1.
    private static async Task<bool> ReadAuthResponseAsync(NetworkStream stream, CancellationToken ct)
    {
        while (true)
        {
            var packet = await ReadAsync(stream, ct).ConfigureAwait(false);
            if (packet.Type == AuthResponseType)
            {
                return packet.Id != RejectedId;
            }
        }
    }

    private static async Task WriteAsync(NetworkStream stream, int id, int type, string body, CancellationToken ct)
    {
        var bodyBytes = Encoding.UTF8.GetBytes(body);
        var packet = new byte[12 + bodyBytes.Length + 2];
        BinaryPrimitives.WriteInt32LittleEndian(packet, packet.Length - 4);
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(4), id);
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(8), type);
        bodyBytes.CopyTo(packet.AsSpan(12));
        await stream.WriteAsync(packet, ct).ConfigureAwait(false);
    }

    private static async Task<RconPacket> ReadAsync(NetworkStream stream, CancellationToken ct)
    {
        var header = new byte[4];
        await stream.ReadExactlyAsync(header, ct).ConfigureAwait(false);
        var size = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (size is < MinPacketSize or > MaxPacketSize)
        {
            throw new InvalidDataException($"Not an RCON packet: size {size}.");
        }

        var data = new byte[size];
        await stream.ReadExactlyAsync(data, ct).ConfigureAwait(false);
        return new RconPacket(
            BinaryPrimitives.ReadInt32LittleEndian(data),
            BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(4)),
            data[8..^2]);
    }

    private readonly record struct RconPacket(int Id, int Type, byte[] Body);
}
