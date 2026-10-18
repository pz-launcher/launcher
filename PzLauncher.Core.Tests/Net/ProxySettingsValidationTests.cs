using PzLauncher.Core.Common;
using PzLauncher.Core.Net;
using PzLauncher.Core.Tests.TestSupport;

namespace PzLauncher.Core.Tests.Net;

public class ProxySettingsValidationTests
{
    private static ProxySettings Manual(string host, int port = 7890, string? username = null, string? password = null) =>
        new(ProxyMode.Manual, new ManualProxy(host, port, username, password));

    [Theory]
    [InlineData("127.0.0.1", "127.0.0.1")]
    [InlineData("  proxy.example.test ", "proxy.example.test")]
    [InlineData("http://proxy.example.test", "proxy.example.test")]
    [InlineData("HTTP://proxy.example.test/", "proxy.example.test")]
    [InlineData("localhost", "localhost")]
    [InlineData("::1", "::1")]
    [InlineData("[::1]", "[::1]")]
    public void Validate_ValidHost_StripsThePrefix(string host, string expected) =>
        Assert.Equal(expected, ResultAssert.Ok(ProxySettingsValidation.Validate(Manual(host))).Manual?.Host);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("http://")]
    [InlineData("https://proxy.example.test")]
    [InlineData("socks5://127.0.0.1")]
    [InlineData("127.0.0.1:7890")]
    [InlineData("7890")]
    [InlineData("127.1")]
    [InlineData("user:pw@proxy.example.test")]
    [InlineData("proxy.example.test/path")]
    [InlineData("bad host")]
    public void Validate_InvalidHost_FailsWithHostInvalid(string host)
    {
        var error = ResultAssert.Failed(ProxySettingsValidation.Validate(Manual(host)), ErrorCode.NetworkProxyHostInvalid);

        Assert.Equal(host, error.Details["host"]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65536)]
    public void Validate_PortOutOfRange_FailsWithPortInvalid(int port) =>
        ResultAssert.Failed(ProxySettingsValidation.Validate(Manual("127.0.0.1", port)), ErrorCode.NetworkProxyPortInvalid);

    [Theory]
    [InlineData(1)]
    [InlineData(65535)]
    public void Validate_PortAtTheLimits_IsValid(int port) =>
        ResultAssert.Ok(ProxySettingsValidation.Validate(Manual("127.0.0.1", port)));

    [Fact]
    public void Validate_ManualModeWithoutManualProxy_FailsWithHostInvalid() =>
        ResultAssert.Failed(ProxySettingsValidation.Validate(new ProxySettings(ProxyMode.Manual, null)), ErrorCode.NetworkProxyHostInvalid);

    [Theory]
    [InlineData(ProxyMode.System)]
    [InlineData(ProxyMode.None)]
    public void Validate_OtherModes_DropTheManualProxy(ProxyMode mode) =>
        Assert.Equal(
            new ProxySettings(mode, null),
            ResultAssert.Ok(ProxySettingsValidation.Validate(new ProxySettings(mode, new ManualProxy("bad host", 0, null, null)))));

    [Theory]
    [InlineData(null, "pw", null, null)]
    [InlineData("", "pw", null, null)]
    [InlineData("alice", "", "alice", null)]
    [InlineData("alice", "pw", "alice", "pw")]
    public void Validate_Credentials_EmptyMeansNone(string? username, string? password, string? expectedUser, string? expectedPassword)
    {
        var manual = ResultAssert.Ok(ProxySettingsValidation.Validate(Manual("127.0.0.1", username: username, password: password))).Manual;

        Assert.Equal((expectedUser, expectedPassword), (manual?.Username, manual?.Password));
    }
}
