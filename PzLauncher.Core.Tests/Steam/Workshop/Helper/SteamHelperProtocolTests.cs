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
    public void Request_ListCollections_HasNoIds()
    {
        var json = JsonSerializer.Serialize(
            new SteamHelperRequest(SteamHelperAction.ListCollections, "/pz/libsteam_api.so", []), SteamHelperJsonContext.Default.SteamHelperRequest);

        Assert.Equal("""{"action":"ListCollections","steamApiPath":"/pz/libsteam_api.so","ids":[]}""", json);
    }

    [Theory]
    [InlineData(WorkshopAction.Subscribe)]
    [InlineData(WorkshopAction.Unsubscribe)]
    [InlineData(WorkshopAction.Update)]
    public void HelperAction_HasEachWorkshopActionsName(WorkshopAction action) =>
        Assert.True(Enum.TryParse<SteamHelperAction>(action.ToString(), out _));

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
            (new SteamHelperFailed(SteamHelperFailure.QueryFailed, "Favorited page 1: EResult 2"), "failed"),
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
    public void Collections_RoundTrips()
    {
        var original = new SteamHelperCollections(new WorkshopUserCollections(
            12345678, [new WorkshopCollectionSummary(3623026433, "合集 Pack", 201), new WorkshopCollectionSummary(2690796635, null, 71)], []));

        var json = JsonSerializer.Serialize<SteamHelperEvent>(original, SteamHelperJsonContext.Default.SteamHelperEvent);
        var copy = Assert.IsType<SteamHelperCollections>(JsonSerializer.Deserialize(json, SteamHelperJsonContext.Default.SteamHelperEvent));

        Assert.StartsWith("""{"event":"collections","collections":{"accountId":12345678,""", json, StringComparison.Ordinal);
        Assert.Equal(original.Collections.AccountId, copy.Collections.AccountId);
        Assert.Equal(original.Collections.Favorited, copy.Collections.Favorited);
        Assert.Empty(copy.Collections.Published);
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

    [Fact]
    public void ReadUgcQueryCompleted_ReadsTheCountsAfterTheHandle()
    {
        var payload = new byte[280];
        BinaryPrimitives.WriteUInt64LittleEndian(payload, 0xDEADBEEF00000001);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(8), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(12), 50);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(16), 73);

        Assert.Equal(new UgcQueryCompleted(0xDEADBEEF00000001, 1, 50, 73), SteamCallbackPayloads.ReadUgcQueryCompleted(payload));
        Assert.Null(SteamCallbackPayloads.ReadUgcQueryCompleted(payload[..19]));
    }

    [Theory]
    [InlineData(true, 9784, 9768)]
    [InlineData(false, 9772, 9760)]
    public void ReadUgcDetails_ReadsTheFieldsAtThePackingsOffsets(bool largePack, int size, int childCountOffset)
    {
        var details = UgcDetailsBuffer(size, 3623026433, "Пак 合集 ✓", childCountOffset, 201);

        Assert.Equal(new UgcDetails(3623026433, 1, 2, "Пак 合集 ✓", 201), SteamCallbackPayloads.ReadUgcDetails(details, largePack));
        Assert.Null(SteamCallbackPayloads.ReadUgcDetails(details.AsSpan(0, childCountOffset + 3), largePack));
    }

    [Fact]
    public void ReadUgcDetails_TitleFillingItsField_EndsAtTheField()
    {
        var details = UgcDetailsBuffer(9784, 1, new string('a', 129), 9768, 0);
        details[24 + 129] = (byte)'b';

        Assert.Equal(new string('a', 129), SteamCallbackPayloads.ReadUgcDetails(details, largePack: true)?.Title);
    }

    [Fact]
    public void ReadAllPages_ReadsUntilAShortPage()
    {
        List<uint> asked = [];
        List<WorkshopCollectionSummary> collections = [];

        var failed = SteamHelperSession.ReadAllPages(
            SteamUserUgcList.Favorited,
            page =>
            {
                asked.Add(page);
                return page == 1 ? Page(70, Range(1, 50)) : Page(70, Range(51, 20));
            },
            collections);

        Assert.Null(failed);
        Assert.Equal([1u, 2u], asked);
        Assert.Equal(Enumerable.Range(1, 70).Select(id => (ulong)id), collections.Select(collection => collection.Id));
        Assert.Equal(new WorkshopCollectionSummary(51, "Collection 51", 51), collections[50]);
    }

    [Fact]
    public void ReadAllPages_FullLastPage_StopsAtSteamsTotal()
    {
        List<uint> asked = [];

        var failed = SteamHelperSession.ReadAllPages(
            SteamUserUgcList.Published,
            page =>
            {
                asked.Add(page);
                return Page(100, Range((page * 50) - 49, 50));
            },
            []);

        Assert.Null(failed);
        Assert.Equal([1u, 2u], asked);
    }

    [Fact]
    public void ReadAllPages_SteamKeepsAnsweringFullPages_StopsAtTheBound()
    {
        List<uint> asked = [];

        var failed = SteamHelperSession.ReadAllPages(
            SteamUserUgcList.Favorited,
            page =>
            {
                asked.Add(page);
                return Page(uint.MaxValue, Range(1, 50));
            },
            []);

        Assert.Null(failed);
        Assert.Equal(SteamHelperSession.MaxPages, (uint)asked.Count);
    }

    [Fact]
    public void ReadAllPages_SkipsFailedItemsNonCollectionsAndRepeats_AndKeepsAnUntitledOne()
    {
        List<WorkshopCollectionSummary> collections = [];
        UgcDetails[] items =
        [
            new(1, 1, 2, "", 3),
            new(2, 9, 2, "Gone", 0),
            new(3, 1, 0, "An item", 0),
            new(1, 1, 2, "Repeat", 3),
        ];

        Assert.Null(SteamHelperSession.ReadAllPages(SteamUserUgcList.Favorited, _ => Page(4, items), collections));

        Assert.Equal([new WorkshopCollectionSummary(1, null, 3)], collections);
    }

    [Fact]
    public void ReadAllPages_PageFails_ReportsTheListPageAndResult()
    {
        var failed = SteamHelperSession.ReadAllPages(
            SteamUserUgcList.Published, page => page == 1 ? Page(80, Range(1, 50)) : new SteamUgcPage(2, 0, 0, []), []);

        Assert.NotNull(failed);
        Assert.Equal(SteamHelperFailure.QueryFailed, failed.Reason);
        Assert.Contains("Published page 2", failed.Message, StringComparison.Ordinal);
        Assert.Contains("EResult 2", failed.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadAllPages_NoAnswer_SaysSo()
    {
        var failed = SteamHelperSession.ReadAllPages(SteamUserUgcList.Favorited, _ => SteamUgcPage.NoAnswer, []);

        Assert.NotNull(failed);
        Assert.Equal(SteamHelperFailure.QueryFailed, failed.Reason);
        Assert.Contains("Favorited page 1: no answer", failed.Message, StringComparison.Ordinal);
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

    [Theory]
    [InlineData(nameof(SteamHelperAction.Update))]
    [InlineData(nameof(SteamHelperAction.ListCollections))]
    public void Host_LibraryMissing_WritesLibraryNotLoaded(string actionName)
    {
        var action = Enum.Parse<SteamHelperAction>(actionName);
        using var temp = new TempDirectory();
        var request = JsonSerializer.Serialize(
            new SteamHelperRequest(action, Path.Combine(temp.Path, "steam_api64.dll"), action == SteamHelperAction.Update ? [1] : []),
            SteamHelperJsonContext.Default.SteamHelperRequest);

        var (exitCode, events) = RunHost(request);

        Assert.Equal(1, exitCode);
        Assert.Equal(SteamHelperFailure.LibraryNotLoaded, Assert.IsType<SteamHelperFailed>(Assert.Single(events)).Reason);
    }

    private static SteamUgcPage Page(uint total, UgcDetails[] items) => new(1, (uint)items.Length, total, items);

    private static UgcDetails[] Range(uint first, int count) =>
        [.. Enumerable.Range(0, count).Select(i => new UgcDetails(first + (uint)i, 1, 2, $"Collection {first + (uint)i}", first + (uint)i))];

    private static byte[] UgcDetailsBuffer(int size, ulong id, string title, int childCountOffset, uint childCount)
    {
        var details = new byte[size];
        BinaryPrimitives.WriteUInt64LittleEndian(details, id);
        BinaryPrimitives.WriteInt32LittleEndian(details.AsSpan(8), 1);
        BinaryPrimitives.WriteInt32LittleEndian(details.AsSpan(12), 2);
        Encoding.UTF8.GetBytes(title).CopyTo(details.AsSpan(24));
        BinaryPrimitives.WriteUInt32LittleEndian(details.AsSpan(childCountOffset), childCount);
        return details;
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
