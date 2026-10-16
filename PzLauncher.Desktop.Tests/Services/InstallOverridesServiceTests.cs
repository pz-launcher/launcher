using PzLauncher.Core.Launch;
using PzLauncher.Desktop.Services;
using PzLauncher.Desktop.Tests.TestSupport;

namespace PzLauncher.Desktop.Tests.Services;

public class InstallOverridesServiceTests
{
    private const string GameRoot = @"E:\SteamLibrary\steamapps\common\ProjectZomboid";
    private const string OtherRoot = @"D:\Games\ProjectZomboid";

    private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(300);

    private static readonly GameLaunchOverrides Saved = new(@"C:\Java\jdk-25", 8192, 2048, "-XX:+UseZGC", "-debug", GarbageCollector.Z);

    private readonly FakePreferenceStore _store = new();
    private readonly ManualTimeProvider _time = new();

    private InstallOverridesService CreateService(StringComparison pathComparison = StringComparison.OrdinalIgnoreCase) =>
        new(
            _store,
            new UserPreferences(null, null, null) { InstallOverrides = new Dictionary<string, GameLaunchOverrides> { [GameRoot] = Saved } },
            new FakePlatformEnvironment { PathComparison = pathComparison },
            _time);

    [Fact]
    public void Get_NothingSaved_ReturnsEmpty() => Assert.Same(GameLaunchOverrides.Empty, CreateService().Get(OtherRoot));

    [Theory]
    [InlineData(StringComparison.OrdinalIgnoreCase, true)]
    [InlineData(StringComparison.Ordinal, false)]
    public void Get_PathInOtherCase_FollowsThePlatformComparison(StringComparison comparison, bool found) =>
        Assert.Equal(found ? Saved : GameLaunchOverrides.Empty, CreateService(comparison).Get(GameRoot.ToUpperInvariant()));

    [Fact]
    public void Save_AppliesAtOnce_WritesTheLatestValueOnceAfterTheDelay()
    {
        var service = CreateService();
        var first = Saved with { MaxMemoryMb = 4096 };
        var last = Saved with { MaxMemoryMb = 4608 };

        service.Save(GameRoot, first);
        _time.Advance(SaveDelay / 2);
        service.Save(GameRoot, last);
        _time.Advance(SaveDelay / 2);

        Assert.Equal(last, service.Get(GameRoot));
        Assert.Empty(_store.SavedInstallOverrides);

        _time.Advance(SaveDelay / 2);

        Assert.Equal([(GameRoot, last)], _store.SavedInstallOverrides);

        _time.Advance(SaveDelay * 2);

        Assert.Single(_store.SavedInstallOverrides);
    }

    [Fact]
    public void Save_PathInOtherCase_WritesUnderTheSavedSpelling()
    {
        var service = CreateService();
        var changed = Saved with { ExtraGameArgs = "" };

        service.Save(GameRoot.ToUpperInvariant(), changed);
        _time.Advance(SaveDelay);

        Assert.Equal([(GameRoot, changed)], _store.SavedInstallOverrides);
        Assert.Equal(changed, service.Get(GameRoot));
    }

    [Fact]
    public void Save_TwoInstalls_WritesEach()
    {
        var service = CreateService();
        var other = GameLaunchOverrides.Empty with { ExtraGameArgs = "-nosteam" };

        service.Save(GameRoot, Saved);
        service.Save(OtherRoot, other);
        _time.Advance(SaveDelay);

        Assert.Equal([(GameRoot, Saved), (OtherRoot, other)], _store.SavedInstallOverrides);
        Assert.Equal(other, service.Get(OtherRoot));
        Assert.Equal(Saved, service.Get(GameRoot));
    }

    [Fact]
    public async Task FlushAsync_PendingWrites_WritesThemNowAndOnlyOnce()
    {
        var service = CreateService();
        var other = GameLaunchOverrides.Empty with { ExtraGameArgs = "-nosteam" };
        service.Save(GameRoot, Saved);
        service.Save(OtherRoot, other);

        await service.FlushAsync(TestContext.Current.CancellationToken);

        Assert.Equal([(GameRoot, Saved), (OtherRoot, other)], _store.SavedInstallOverrides);

        _time.Advance(SaveDelay);

        Assert.Equal(2, _store.SavedInstallOverrides.Count);
    }

    [Fact]
    public async Task FlushAsync_NothingPending_CompletesWithoutWriting()
    {
        await CreateService().FlushAsync(TestContext.Current.CancellationToken);

        Assert.Empty(_store.SavedInstallOverrides);
    }

    [Fact]
    public async Task FlushAsync_WriteInFlight_CompletesWhenItFinishes()
    {
        var service = CreateService();
        _store.InstallOverridesSaveGate = new TaskCompletionSource();
        service.Save(GameRoot, Saved);
        _time.Advance(SaveDelay);

        var flush = service.FlushAsync(TestContext.Current.CancellationToken);

        Assert.False(flush.IsCompleted);

        _store.InstallOverridesSaveGate.SetResult();
        await flush;

        Assert.Single(_store.SavedInstallOverrides);
    }

    [Fact]
    public async Task FlushAsync_Cancelled_Throws()
    {
        var service = CreateService();
        _store.InstallOverridesSaveGate = new TaskCompletionSource();
        service.Save(GameRoot, Saved);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.FlushAsync(new CancellationToken(canceled: true)));
    }
}
