using PzLauncher.Core.Mod.ZombieBuddy;
using PzLauncher.Desktop.Services;
using PzLauncher.Desktop.Tests.TestSupport;
using static PzLauncher.Desktop.Tests.TestSupport.TestZombieBuddy;

namespace PzLauncher.Desktop.Tests.Services;

public class ZombieBuddyChannelServiceTests
{
    private const string GameRoot = @"E:\SteamLibrary\steamapps\common\ProjectZomboid";
    private const string OtherRoot = @"D:\Games\ProjectZomboid";

    private readonly FakePreferenceStore _store = new();

    private ZombieBuddyChannelService CreateService(StringComparison pathComparison = StringComparison.OrdinalIgnoreCase) =>
        new(
            _store,
            new UserPreferences(null, null, null) { ZombieBuddyChannels = new Dictionary<string, ZombieBuddyChannel> { [GameRoot] = ZombieBuddyChannel.Beta } },
            new FakePlatformEnvironment { PathComparison = pathComparison });

    [Fact]
    public void Get_NothingSaved_ReturnsStable() => Assert.Equal(ZombieBuddyChannel.Stable, CreateService().Get(OtherRoot));

    [Theory]
    [InlineData(StringComparison.OrdinalIgnoreCase, ZombieBuddyChannel.Beta)]
    [InlineData(StringComparison.Ordinal, ZombieBuddyChannel.Stable)]
    public void Get_PathInOtherCase_FollowsThePlatformComparison(StringComparison comparison, ZombieBuddyChannel expected) =>
        Assert.Equal(expected, CreateService(comparison).Get(GameRoot.ToUpperInvariant()));

    [Fact]
    public void Save_AppliesAtOnceAndWritesIt()
    {
        var service = CreateService();

        service.Save(OtherRoot, ZombieBuddyChannel.Beta);

        Assert.Equal(ZombieBuddyChannel.Beta, service.Get(OtherRoot));
        Assert.Equal([(OtherRoot, ZombieBuddyChannel.Beta)], _store.SavedZombieBuddyChannels);
    }

    [Fact]
    public void Save_PathInOtherCase_WritesUnderTheSavedSpelling()
    {
        var service = CreateService();

        service.Save(GameRoot.ToUpperInvariant(), ZombieBuddyChannel.Stable);

        Assert.Equal([(GameRoot, ZombieBuddyChannel.Stable)], _store.SavedZombieBuddyChannels);
        Assert.Equal(ZombieBuddyChannel.Stable, service.Get(GameRoot));
    }

    [Fact]
    public void EffectiveItem_SavedChannelSubscribed_ReturnsIt() =>
        Assert.Equal(BetaItem, CreateService().EffectiveItem(GameRoot, [StableItem, BetaItem]));

    [Fact]
    public void EffectiveItem_SavedChannelNotSubscribed_ReturnsTheSubscribedOne() =>
        Assert.Equal(StableItem, CreateService().EffectiveItem(GameRoot, [StableItem]));

    [Fact]
    public void EffectiveItem_NothingSubscribed_ReturnsNull() => Assert.Null(CreateService().EffectiveItem(GameRoot, []));
}
