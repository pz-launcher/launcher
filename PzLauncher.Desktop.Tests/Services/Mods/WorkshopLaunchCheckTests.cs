using PzLauncher.Core.Common;
using PzLauncher.Core.Steam.Workshop;
using PzLauncher.Desktop.Services.Mods;
using PzLauncher.Desktop.Tests.TestSupport;

namespace PzLauncher.Desktop.Tests.Services.Mods;

public class WorkshopLaunchCheckTests
{
    private const string GameRoot = @"E:\SteamLibrary\steamapps\common\ProjectZomboid";
    private const string GameArgs = @"-cachedir=D:\pz";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly FakeWorkshopService _workshop = new();
    private readonly ManualTimeProvider _time = new();

    private Task<IReadOnlyList<WorkshopItem>> FindAsync(string? collection) =>
        TestWorkshop.LaunchCheck(_workshop, _time).FindOutdatedAsync(GameRoot, GameArgs, collection);

    private void ArrangeOutdatedItem() =>
        _workshop.WithItem(TestWorkshop.Item(1, mods: ["ModA"])).WithPublished(1, "Alpha", "200");

    [Fact]
    public async Task OutdatedItem_IsReturnedWithTheLatestVersion()
    {
        ArrangeOutdatedItem();

        var item = Assert.Single(await FindAsync("Pack"));

        Assert.Equal(("Alpha", "200", WorkshopItemState.NeedsUpdate), (item.Latest?.Title, item.Latest?.Manifest, item.State));
        Assert.Equal([(GameRoot, GameArgs, (string?)"Pack")], _workshop.LaunchRequests);
    }

    [Fact]
    public async Task CheckFails_ReturnsNothing()
    {
        ArrangeOutdatedItem();
        _workshop.CheckError = () => TestErrors.Create(ErrorCode.WorkshopNetworkFailed);

        Assert.Empty(await FindAsync(null));
    }

    [Fact]
    public async Task TooSlow_ReturnsNothingAfterTheTimeout()
    {
        ArrangeOutdatedItem();
        _workshop.CheckGate = new TaskCompletionSource();

        var find = FindAsync("Pack");
        _time.Advance(Timeout - TimeSpan.FromSeconds(1));

        Assert.False(find.IsCompleted);

        _time.Advance(TimeSpan.FromSeconds(1));

        Assert.Empty(await find);
    }
}
