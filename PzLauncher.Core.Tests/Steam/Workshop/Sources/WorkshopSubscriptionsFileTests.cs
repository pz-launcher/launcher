using Microsoft.Extensions.Logging.Abstractions;
using PzLauncher.Core.Common;
using PzLauncher.Core.Steam.Workshop;
using PzLauncher.Core.Steam.Workshop.Sources;
using PzLauncher.Core.Tests.TestSupport;

namespace PzLauncher.Core.Tests.Steam.Workshop.Sources;

public sealed class WorkshopSubscriptionsFileTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Result<IReadOnlyList<WorkshopSubscription>> Parse(string text) =>
        WorkshopSubscriptionsFile.Parse(text, "p", NullLogger.Instance);

    [Fact]
    public async Task ReadAsync_RealShape_ListsItemsInFileOrder()
    {
        var items = ResultAssert.Ok(await WorkshopSubscriptionsFile.ReadAsync(Fixtures.Path("Steam", "108600_subscriptions.vdf"), NullLogger.Instance, Ct));

        Assert.Equal(
            [
                new WorkshopSubscription(3778421142, DateTimeOffset.FromUnixTimeSeconds(1792336752), false),
                new WorkshopSubscription(3811390574, DateTimeOffset.FromUnixTimeSeconds(1792336673), false),
                new WorkshopSubscription(2409333430, DateTimeOffset.FromUnixTimeSeconds(1792225503), true),
            ],
            items);
    }

    [Fact]
    public async Task ReadAsync_MissingFile_IsNoSubscriptions()
    {
        var items = ResultAssert.Ok(await WorkshopSubscriptionsFile.ReadAsync(Path.Combine(_temp.Path, "ugc", "108600_subscriptions.vdf"), NullLogger.Instance, Ct));

        Assert.Empty(items);
    }

    [Fact]
    public async Task ReadAsync_UnreadableFile_IsSubscriptionsUnreadable()
    {
        var error = ResultAssert.Failed(await WorkshopSubscriptionsFile.ReadAsync(_temp.Path, NullLogger.Instance, Ct), ErrorCode.WorkshopSubscriptionsUnreadable);

        Assert.Equal(_temp.Path, error.Details["path"]);
    }

    [Theory]
    [InlineData("\"subscribedfiles\"\n{\n\t\"0\"\n\t{\n")]
    [InlineData("\"other\"\n{\n}\n")]
    [InlineData("\"subscribedfiles\"\t\"flat\"\n")]
    public void Parse_MalformedFile_IsSubscriptionsUnreadable(string text) =>
        ResultAssert.Failed(Parse(text), ErrorCode.WorkshopSubscriptionsUnreadable);

    [Fact]
    public void Parse_MalformedEntries_AreSkippedAndTheRestKept()
    {
        var items = ResultAssert.Ok(Parse("""
            "subscribedfiles"
            {
                "appid" "108600"
                "0" { "publishedfileid" "abc" "time_subscribed" "1" }
                "1" { "publishedfileid" "0" "time_subscribed" "1" }
                "2" { "publishedfileid" "5" "time_subscribed" "-1" }
                "3" "flat"
                "4" { "time_subscribed" "1" }
                "5" { "publishedfileid" "7" "time_subscribed" "2" }
                "6" { "publishedfileid" "7" "time_subscribed" "3" }
            }
            """));

        Assert.Equal([new WorkshopSubscription(7, DateTimeOffset.FromUnixTimeSeconds(2), false)], items);
    }
}
