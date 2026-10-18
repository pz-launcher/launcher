using PzLauncher.Core.Common;
using PzLauncher.Core.Net;
using PzLauncher.Core.Platform;
using PzLauncher.Desktop.Localization;
using PzLauncher.Desktop.Services;
using PzLauncher.Desktop.Tests.TestSupport;
using PzLauncher.Desktop.ViewModels.Net;

namespace PzLauncher.Desktop.Tests.ViewModels.Net;

[Collection(UiTextCultureCollection.Name)]
public class ProxySettingsViewModelTests : UiTextCultureTest
{
    private static readonly TimeSpan Delay = TimeSpan.FromMilliseconds(300);

    private static readonly SavedProxy Manual = new(ProxyMode.Manual, "127.0.0.1", 7890, "alice", "secret");

    private readonly FakeNetworkService _network = new();
    private readonly FakePreferenceStore _store = new();
    private readonly ManualTimeProvider _time = new();

    private ProxySettingsViewModel Create(SavedProxy? saved = null, PlatformKind os = PlatformKind.Windows) =>
        new(TestProxy.Service(_network, _store, saved, _time), _network, new FakePlatformEnvironment { Os = os }, Localization);

    private static ConnectionTestResult Ok(string name, int ms) => new(name, new Uri("https://example.test/"), TimeSpan.FromMilliseconds(ms), null);

    [Fact]
    public void Constructor_ShowsTheSavedChoiceWithoutApplyingItAgain()
    {
        var vm = Create(Manual);

        Assert.Equal(
            (ProxyMode.Manual, "127.0.0.1", "7890", "alice", "secret", true),
            (vm.Mode, vm.Host, vm.Port, vm.Username, vm.Password, vm.IsManual));
        Assert.Null(vm.ErrorText);
        Assert.Single(_network.Applied);
        Assert.Empty(_store.SavedProxies);
    }

    [Fact]
    public void Constructor_NothingSaved_ShowsSystemWithEmptyFields()
    {
        var vm = Create();

        Assert.Equal((ProxyMode.System, "", "", false), (vm.Mode, vm.Host, vm.Port, vm.IsManual));
    }

    [Fact]
    public void Mode_Changed_AppliesAndSavesAtOnceKeepingTheManualFields()
    {
        var vm = Create(Manual);

        vm.Mode = ProxyMode.None;

        Assert.Equal(new ProxySettings(ProxyMode.None, null), _network.Proxy);
        Assert.Equal([Manual with { Mode = ProxyMode.None }], _store.SavedProxies);
        Assert.False(vm.IsManual);
    }

    [Fact]
    public void Mode_ManualWithInvalidFields_ShowsTheErrorAndKeepsThePreviousModeInEffect()
    {
        var vm = Create();

        vm.Mode = ProxyMode.Manual;

        Assert.Equal(ErrorMessages.ToUserText(TestErrors.Create(ErrorCode.NetworkProxyHostInvalid)), vm.ErrorText);
        Assert.Equal(ProxySettings.Default, _network.Proxy);
        Assert.Empty(_store.SavedProxies);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("65536")]
    [InlineData("-1")]
    [InlineData("")]
    public void Port_Invalid_ShowsTheErrorAndNeitherAppliesNorSaves(string port)
    {
        var vm = Create(Manual);

        vm.Port = port;
        _time.Advance(Delay * 2);

        Assert.Equal(ErrorMessages.ToUserText(TestErrors.Create(ErrorCode.NetworkProxyPortInvalid)), vm.ErrorText);
        Assert.Single(_network.Applied);
        Assert.Empty(_store.SavedProxies);
    }

    [Theory]
    [InlineData("bad host")]
    [InlineData("https://127.0.0.1")]
    [InlineData("127.0.0.1:7890")]
    public void Host_Invalid_ShowsTheErrorAndNeitherAppliesNorSaves(string host)
    {
        var vm = Create(Manual);

        vm.Host = host;
        _time.Advance(Delay * 2);

        Assert.Equal(ErrorMessages.ToUserText(TestErrors.Create(ErrorCode.NetworkProxyHostInvalid)), vm.ErrorText);
        Assert.Single(_network.Applied);
        Assert.Empty(_store.SavedProxies);
    }

    [Fact]
    public void ValidEdit_ClearsTheErrorAndAppliesAndSavesAfterTheDebounce()
    {
        var vm = Create(Manual);
        vm.Port = "0";

        vm.Port = "7891";
        vm.Host = "http://proxy.example.test";

        Assert.Null(vm.ErrorText);
        Assert.Single(_network.Applied);

        _time.Advance(Delay);

        Assert.Equal(new ProxySettings(ProxyMode.Manual, new ManualProxy("proxy.example.test", 7891, "alice", "secret")), _network.Proxy);
        Assert.Equal([Manual with { Host = "http://proxy.example.test", Port = 7891 }], _store.SavedProxies);
    }

    [Fact]
    public void EmptyCredentials_AreSavedAsNone()
    {
        var vm = Create(Manual);

        vm.Username = "";
        vm.Password = "";
        _time.Advance(Delay);

        Assert.Equal(new ManualProxy("127.0.0.1", 7890, null, null), _network.Proxy.Manual);
        Assert.Equal(Manual with { Username = null, Password = null }, _store.SavedProxies[^1]);
    }

    [Fact]
    public void ErrorText_FollowsTheLanguage()
    {
        var vm = Create(Manual);
        vm.Port = "0";
        var english = vm.ErrorText;

        Localization.SetLanguage(AppLanguage.Russian);

        Assert.NotNull(vm.ErrorText);
        Assert.NotEqual(english, vm.ErrorText);
    }

    [Theory]
    [InlineData(PlatformKind.Windows, false)]
    [InlineData(PlatformKind.Linux, true)]
    [InlineData(PlatformKind.MacOS, true)]
    public void PlainTextPasswordNote_ShowsOffWindows(PlatformKind os, bool shown) =>
        Assert.Equal(shown, Create(os: os).IsPasswordStoredInPlainText);

    [Fact]
    public void SystemHint_DiffersPerOs()
    {
        var hints = Enum.GetValues<PlatformKind>().Select(os => Create(os: os).SystemHint).ToList();

        Assert.All(hints, hint => Assert.False(string.IsNullOrWhiteSpace(hint)));
        Assert.Equal(hints.Count, hints.Distinct().Count());
    }

    [Fact]
    public async Task TestConnection_ShowsARowPerSite()
    {
        _network.Results = () =>
        [
            Ok("GitHub", 123),
            new ConnectionTestResult("Gitee", new Uri("https://example.test/"), TimeSpan.FromSeconds(15), TestErrors.Create(ErrorCode.NetworkTimeout)),
        ];
        var vm = Create();

        await vm.TestConnectionCommand.ExecuteAsync(null);

        Assert.Equal(
            [
                new ConnectionTestRow("GitHub", "OK (123 ms)", IsError: false),
                new ConnectionTestRow("Gitee", ErrorMessages.ToUserText(TestErrors.Create(ErrorCode.NetworkTimeout)), IsError: true),
            ],
            vm.TestResults);
    }

    [Fact]
    public async Task TestConnection_UsesAnEditStillWaitingForItsDebounce()
    {
        var vm = Create(Manual);
        vm.Port = "7891";

        await vm.TestConnectionCommand.ExecuteAsync(null);

        Assert.Equal(7891, Assert.Single(_network.Tests).Manual?.Port);
    }

    [Fact]
    public async Task TestConnection_WhileRunning_IsDisabledAndClearsOldRows()
    {
        _network.Results = () => [Ok("GitHub", 1), Ok("Gitee", 2)];
        var vm = Create();
        await vm.TestConnectionCommand.ExecuteAsync(null);
        _network.TestGate = new TaskCompletionSource();

        var running = vm.TestConnectionCommand.ExecuteAsync(null);

        Assert.False(vm.TestConnectionCommand.CanExecute(null));
        Assert.Empty(vm.TestResults);

        _network.TestGate.SetResult();
        await running;

        Assert.True(vm.TestConnectionCommand.CanExecute(null));
        Assert.Equal(2, vm.TestResults.Count);
    }

    [Fact]
    public async Task TestRows_FollowTheLanguage()
    {
        _network.Results = () => [Ok("GitHub", 5)];
        var vm = Create();
        await vm.TestConnectionCommand.ExecuteAsync(null);

        Localization.SetLanguage(AppLanguage.SimplifiedChinese);

        Assert.Equal("正常（5 毫秒）", Assert.Single(vm.TestResults).Text);
    }
}
