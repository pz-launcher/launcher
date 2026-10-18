using PzLauncher.Core.Common;
using PzLauncher.Core.Net;
using PzLauncher.Desktop.Services;
using PzLauncher.Desktop.Tests.TestSupport;

namespace PzLauncher.Desktop.Tests.Services;

public class ProxySettingsServiceTests
{
    private static readonly TimeSpan Delay = TimeSpan.FromMilliseconds(300);

    private static readonly SavedProxy Manual = new(ProxyMode.Manual, "127.0.0.1", 7890, "alice", "secret");

    private static readonly ProxySettings ManualSettings = new(ProxyMode.Manual, new ManualProxy("127.0.0.1", 7890, "alice", "secret"));

    private readonly FakeNetworkService _network = new();
    private readonly FakePreferenceStore _store = new();
    private readonly ManualTimeProvider _time = new();

    private ProxySettingsService CreateService(SavedProxy? saved = null) => TestProxy.Service(_network, _store, saved, _time);

    [Fact]
    public void Constructor_NothingSaved_AppliesTheSystemProxy()
    {
        var service = CreateService();

        Assert.Equal([ProxySettings.Default], _network.Applied);
        Assert.Equal(SavedProxy.Default, service.Current);
        Assert.Empty(_store.SavedProxies);
    }

    [Fact]
    public void Constructor_SavedManual_AppliesItWithoutSaving()
    {
        var service = CreateService(Manual);

        Assert.Equal([ManualSettings], _network.Applied);
        Assert.Equal(Manual, service.Current);
        Assert.Empty(_store.SavedProxies);
    }

    [Fact]
    public void Constructor_SavedManualWithoutAPort_UsesTheSystemProxyAndKeepsTheFields()
    {
        var service = CreateService(Manual with { Port = null });

        Assert.Equal([ProxySettings.Default], _network.Applied);
        Assert.Equal(Manual with { Mode = ProxyMode.System, Port = null }, service.Current);
    }

    [Fact]
    public void Apply_Valid_AppliesAndSavesAtOnce()
    {
        var service = CreateService();
        var none = new SavedProxy(ProxyMode.None, "127.0.0.1", 7890, null, null);

        Assert.True(service.Apply(none).IsSuccess);

        Assert.Equal(new ProxySettings(ProxyMode.None, null), _network.Applied[^1]);
        Assert.Equal([none], _store.SavedProxies);
        Assert.Equal(none, service.Current);
    }

    [Fact]
    public void Apply_Invalid_ReturnsTheErrorAndChangesNothing()
    {
        var service = CreateService();

        var result = service.Apply(Manual with { Host = "bad host" });

        Assert.Equal(ErrorCode.NetworkProxyHostInvalid, result.Error?.Code);
        Assert.Single(_network.Applied);
        Assert.Empty(_store.SavedProxies);
        Assert.Equal(SavedProxy.Default, service.Current);
    }

    [Fact]
    public void ApplyDebounced_Valid_AppliesAndSavesTheLatestOnceAfterTheDelay()
    {
        var service = CreateService(Manual);
        var last = Manual with { Port = 7891 };

        Assert.True(service.ApplyDebounced(Manual with { Port = 789 }).IsSuccess);
        _time.Advance(Delay / 2);
        Assert.True(service.ApplyDebounced(last).IsSuccess);
        _time.Advance(Delay / 2);

        Assert.Single(_network.Applied);
        Assert.Empty(_store.SavedProxies);

        _time.Advance(Delay / 2);

        Assert.Equal(new ProxySettings(ProxyMode.Manual, new ManualProxy("127.0.0.1", 7891, "alice", "secret")), _network.Applied[^1]);
        Assert.Equal([last], _store.SavedProxies);
        Assert.Equal(last, service.Current);

        _time.Advance(Delay * 2);

        Assert.Single(_store.SavedProxies);
    }

    [Fact]
    public void ApplyDebounced_Invalid_ReturnsTheErrorAndDropsThePendingChange()
    {
        var service = CreateService(Manual);

        service.ApplyDebounced(Manual with { Port = 7891 });
        var result = service.ApplyDebounced(Manual with { Port = 0 });
        _time.Advance(Delay * 2);

        Assert.Equal(ErrorCode.NetworkProxyPortInvalid, result.Error?.Code);
        Assert.Single(_network.Applied);
        Assert.Empty(_store.SavedProxies);
        Assert.Equal(Manual, service.Current);
    }

    [Fact]
    public void Apply_DropsAPendingDebouncedChange()
    {
        var service = CreateService(Manual);
        var none = Manual with { Mode = ProxyMode.None };

        service.ApplyDebounced(Manual with { Port = 7891 });
        service.Apply(none);
        _time.Advance(Delay * 2);

        Assert.Equal([none], _store.SavedProxies);
        Assert.Equal(new ProxySettings(ProxyMode.None, null), _network.Proxy);
    }

    [Fact]
    public async Task FlushAsync_AppliesThePendingChangeNowAndWaitsForTheWrite()
    {
        var service = CreateService(Manual);
        var edited = Manual with { Port = 7891 };
        _store.ProxySaveGate = new TaskCompletionSource();

        service.ApplyDebounced(edited);
        var flush = service.FlushAsync(TestContext.Current.CancellationToken);

        Assert.Equal([edited], _store.SavedProxies);
        Assert.Equal(7891, _network.Proxy.Manual?.Port);
        Assert.False(flush.IsCompleted);

        _store.ProxySaveGate.SetResult();
        await flush;
        _time.Advance(Delay * 2);

        Assert.Single(_store.SavedProxies);
    }
}
