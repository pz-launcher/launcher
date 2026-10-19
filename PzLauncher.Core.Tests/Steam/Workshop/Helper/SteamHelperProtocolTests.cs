using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using PzLauncher.Core.Steam.Workshop;
using PzLauncher.Core.Steam.Workshop.Actions;
using PzLauncher.Core.Steam.Workshop.Helper;
using PzLauncher.Core.Tests.TestSupport;

namespace PzLauncher.Core.Tests.Steam.Workshop.Helper;

public sealed class SteamHelperProtocolTests
{
    [Fact]
    public void Request_RoundTrips_WithStringAction()
    {
        var original = new SteamHelperRequest(
            SteamHelperAction.Unsubscribe, @"E:\SteamLibrary\steamapps\common\ProjectZomboid\steam_api64.dll", [3811390574, 18446744073709551615]);

        var json = JsonSerializer.Serialize(original, SteamHelperJsonContext.Default.SteamHelperRequest);
        var copy = JsonSerializer.Deserialize(json, SteamHelperJsonContext.Default.SteamHelperRequest);

        Assert.Contains("\"action\":\"Unsubscribe\"", json, StringComparison.Ordinal);
        Assert.Contains("18446744073709551615", json, StringComparison.Ordinal);
        Assert.NotNull(copy);
        Assert.Equal((original.Action, original.SteamApiPath), (copy.Action, copy.SteamApiPath));
        Assert.Equal(original.Ids, copy.Ids);
    }

    [Fact]
    public void Events_RoundTrip_ThroughTheBaseTypeWithTheirDiscriminator()
    {
        (SteamHelperEvent Event, string Name)[] cases =
        [
            (new SteamHelperStarted(), "started"),
            (new SteamHelperProgress(1, 38999824, 253378238), "progress"),
            (new SteamHelperItemDone(2), "itemDone"),
            (new SteamHelperItemFailed(3, WorkshopItemFailure.UnsubscribeFailed, 2), "itemFailed"),
            (new SteamHelperFailed(SteamHelperFailure.SteamNotRunning, "no Steam"), "failed"),
            (new SteamHelperFinished(), "finished"),
        ];

        foreach (var (e, name) in cases)
        {
            var json = JsonSerializer.Serialize(e, SteamHelperJsonContext.Default.SteamHelperEvent);

            Assert.StartsWith($"{{\"event\":\"{name}\"", json, StringComparison.Ordinal);
            Assert.Equal(e, JsonSerializer.Deserialize(json, SteamHelperJsonContext.Default.SteamHelperEvent));
        }
    }

    [Fact]
    public void ItemFailed_WritesTheReasonAsText()
    {
        var json = JsonSerializer.Serialize<SteamHelperEvent>(
            new SteamHelperItemFailed(3, WorkshopItemFailure.DownloadFailed, 15), SteamHelperJsonContext.Default.SteamHelperEvent);

        Assert.Equal("""{"event":"itemFailed","id":3,"reason":"DownloadFailed","steamResult":15}""", json);
    }

    [Theory]
    [InlineData(true, 8)]
    [InlineData(false, 4)]
    public void ReadPublishedFileResult_ReadsTheIdAtThePackingsOffset(bool largePack, int idOffset)
    {
        var payload = new byte[idOffset + 8];
        BinaryPrimitives.WriteInt32LittleEndian(payload, 2);
        BinaryPrimitives.WriteUInt64LittleEndian(payload.AsSpan(idOffset), 3811390574);

        Assert.Equal(new PublishedFileResult(2, 3811390574), SteamCallbackPayloads.ReadPublishedFileResult(payload, largePack));
        Assert.Null(SteamCallbackPayloads.ReadPublishedFileResult(payload[..^1], largePack));
    }

    [Theory]
    [InlineData(true, 8)]
    [InlineData(false, 4)]
    public void ReadDownloadItemResult_ReadsTheFieldsAtThePackingsOffsets(bool largePack, int idOffset)
    {
        var payload = new byte[idOffset + 12];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, 108600);
        BinaryPrimitives.WriteUInt64LittleEndian(payload.AsSpan(idOffset), 3811390574);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(idOffset + 8), 1);

        Assert.Equal(new DownloadItemResult(108600, 3811390574, 1), SteamCallbackPayloads.ReadDownloadItemResult(payload, largePack));
        Assert.Null(SteamCallbackPayloads.ReadDownloadItemResult(payload[..^1], largePack));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("""{"action":"Update","steamApiPath":"relative/steam_api64.dll","ids":[1]}""")]
    [InlineData("""{"action":"Update","steamApiPath":"/pz/libsteam_api.so"}""")]
    public void Host_BadRequest_WritesFailedAndReturnsOne(string request)
    {
        var (exitCode, events) = RunHost(request);

        Assert.Equal(1, exitCode);
        Assert.Equal(SteamHelperFailure.BadRequest, Assert.IsType<SteamHelperFailed>(Assert.Single(events)).Reason);
    }

    [Fact]
    public void Host_LibraryMissing_WritesLibraryNotLoaded()
    {
        using var temp = new TempDirectory();
        var request = JsonSerializer.Serialize(
            new SteamHelperRequest(SteamHelperAction.Update, Path.Combine(temp.Path, "steam_api64.dll"), [1]),
            SteamHelperJsonContext.Default.SteamHelperRequest);

        var (exitCode, events) = RunHost(request);

        Assert.Equal(1, exitCode);
        Assert.Equal(SteamHelperFailure.LibraryNotLoaded, Assert.IsType<SteamHelperFailed>(Assert.Single(events)).Reason);
    }

    private static (int ExitCode, List<SteamHelperEvent> Events) RunHost(string request)
    {
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(request + "\n"));
        using var output = new MemoryStream();

        var exitCode = SteamHelperHost.Run(input, output);

        var lines = Encoding.UTF8.GetString(output.ToArray()).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return (exitCode, [.. lines.Select(line => JsonSerializer.Deserialize(line, SteamHelperJsonContext.Default.SteamHelperEvent)!)]);
    }
}
