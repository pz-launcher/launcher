using System.Text.Json.Serialization;

namespace PzLauncher.Core.Server;

/// <summary>
/// The started server, in <c>&lt;StateDir&gt;/server/running.json</c>: the only record of it, so a restarted host finds it
/// again. RCON settings are the ones the server read at start; the ini may have changed since.
/// </summary>
/// <param name="Ready">RCON has answered once.</param>
/// <param name="StopRequestedAt">Set by stop and kill, so the exit isn't reported as a failure.</param>
internal sealed record RunningServer(
    int ProcessId,
    DateTimeOffset ProcessStartTime,
    string Name,
    string InstallRoot,
    string CacheDir,
    int RconPort,
    string RconPassword,
    bool Ready,
    DateTimeOffset? StopRequestedAt);

// The launcher's own file, so not part of CoreJsonContext's contract.
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(RunningServer))]
internal sealed partial class RunningServerJsonContext : JsonSerializerContext;
