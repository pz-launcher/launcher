using System.Net;
using PzLauncher.Core.Net;

namespace PzLauncher.Core.Tests.Net;

public class SwitchableProxyTests
{
    private static readonly Uri GitHub = new("https://api.github.com/repos/pz-launcher/launcher/releases/latest");

    private readonly FakeSystemProxy _system = new();

    private static ProxySettings Manual(string host = "127.0.0.1", int port = 7890, string? username = null, string? password = null) =>
        new(ProxyMode.Manual, new ManualProxy(host, port, username, password));

    [Fact]
    public void System_ReturnsWhatTheSystemProxyReturns()
    {
        var proxy = new SwitchableProxy(_system);

        Assert.Equal(_system.Address, proxy.GetProxy(GitHub));
        Assert.False(proxy.IsBypassed(GitHub));

        _system.Bypassed = true;

        Assert.True(proxy.IsBypassed(GitHub));
    }

    [Theory]
    [InlineData("https://api.github.com/")]
    [InlineData("http://localhost:8080/")]
    [InlineData("https://gitee.com/api/v5/repos/dontcampy/pzl/releases/latest")]
    public void None_BypassesEveryUri(string url)
    {
        var proxy = new SwitchableProxy(_system);

        proxy.Use(new ProxySettings(ProxyMode.None, null));

        Assert.True(proxy.IsBypassed(new Uri(url)));
        Assert.Null(proxy.GetProxy(new Uri(url)));
    }

    [Fact]
    public void Manual_ReturnsHttpHostAndPort()
    {
        var proxy = new SwitchableProxy(_system);

        proxy.Use(Manual("proxy.example.test", 3128));

        Assert.False(proxy.IsBypassed(GitHub));
        Assert.Equal(new Uri("http://proxy.example.test:3128/"), proxy.GetProxy(GitHub));
    }

    [Fact]
    public void Manual_Ipv6Host_IsBracketed()
    {
        var proxy = new SwitchableProxy(_system);

        proxy.Use(Manual("::1", 7890));

        Assert.Equal(new Uri("http://[::1]:7890/"), proxy.GetProxy(GitHub));
    }

    [Theory]
    [InlineData("http://localhost/")]
    [InlineData("http://127.0.0.1:8080/")]
    [InlineData("http://intranet/")]
    public void Manual_LocalAddresses_AreBypassed(string url)
    {
        var proxy = new SwitchableProxy(_system);

        proxy.Use(Manual());

        Assert.True(proxy.IsBypassed(new Uri(url)));
    }

    [Fact]
    public void Use_Switching_ChangesTheAnswerForTheNextRequest()
    {
        var proxy = new SwitchableProxy(_system);
        var manual = new Uri("http://127.0.0.1:7890/");

        proxy.Use(Manual());
        Assert.Equal(manual, proxy.GetProxy(GitHub));

        proxy.Use(new ProxySettings(ProxyMode.None, null));
        Assert.True(proxy.IsBypassed(GitHub));

        proxy.Use(ProxySettings.Default);
        Assert.Equal(_system.Address, proxy.GetProxy(GitHub));
        Assert.False(proxy.IsBypassed(GitHub));
    }

    [Fact]
    public void Credentials_AreOneObjectThatForwardsToTheCurrentManualCredentials()
    {
        var proxy = new SwitchableProxy(_system);
        var credentials = proxy.Credentials;
        var proxyUri = new Uri("http://127.0.0.1:7890/");

        Assert.NotNull(credentials);
        Assert.Null(credentials.GetCredential(proxyUri, "Basic"));

        proxy.Use(Manual(username: "alice", password: "secret"));
        var alice = credentials.GetCredential(proxyUri, "Basic");

        Assert.Equal(("alice", "secret"), (alice?.UserName, alice?.Password));

        proxy.Use(Manual(username: "bob", password: null));
        var bob = credentials.GetCredential(proxyUri, "Basic");

        Assert.Equal(("bob", ""), (bob?.UserName, bob?.Password));

        proxy.Use(Manual());

        Assert.Null(credentials.GetCredential(proxyUri, "Basic"));

        proxy.Use(new ProxySettings(ProxyMode.None, null));
        proxy.Credentials = new NetworkCredential("ignored", "ignored");

        Assert.Same(credentials, proxy.Credentials);
        Assert.Null(credentials.GetCredential(proxyUri, "Basic"));
    }

    [Fact]
    public void Credentials_System_ForwardsToTheSystemProxy()
    {
        _system.Credentials = new NetworkCredential("corp", "pw");
        var proxy = new SwitchableProxy(_system);

        Assert.Equal("corp", proxy.Credentials?.GetCredential(_system.Address, "Basic")?.UserName);
    }

    private sealed class FakeSystemProxy : IWebProxy
    {
        public Uri Address { get; } = new("http://system-proxy.example.test:8080/");

        public bool Bypassed { get; set; }

        public ICredentials? Credentials { get; set; }

        public Uri? GetProxy(Uri destination) => Bypassed ? destination : Address;

        public bool IsBypassed(Uri host) => Bypassed;
    }
}
