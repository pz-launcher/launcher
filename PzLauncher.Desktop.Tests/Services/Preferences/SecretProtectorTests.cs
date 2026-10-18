using PzLauncher.Desktop.Services.Preferences;

namespace PzLauncher.Desktop.Tests.Services.Preferences;

public class SecretProtectorTests
{
    [Theory]
    [InlineData("secret")]
    [InlineData("pä ss:wörd/密码")]
    [InlineData("")]
    public void Plain_RoundTripsAsPrefixedPlainText(string secret)
    {
        var plain = new PlainSecretProtector();

        var stored = plain.Protect(secret);

        Assert.Equal("plain:" + secret, stored);
        Assert.Equal(secret, plain.Unprotect(stored ?? ""));
    }

    [Theory]
    [InlineData("dpapi:AQAAAA==")]
    [InlineData("secret")]
    public void Plain_OtherScheme_IsNull(string stored) => Assert.Null(new PlainSecretProtector().Unprotect(stored));

    [Theory]
    [InlineData("secret")]
    [InlineData("pä ss:wörd/密码")]
    [InlineData("")]
    public void Dpapi_RoundTripsWithoutThePlainText(string secret)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "DPAPI is Windows-only.");
        var dpapi = new WindowsDpapiSecretProtector();

        var stored = dpapi.Protect(secret);

        Assert.NotNull(stored);
        Assert.StartsWith("dpapi:", stored, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", stored, StringComparison.Ordinal);
        Assert.Equal(secret, dpapi.Unprotect(stored));
    }

    [Theory]
    [InlineData("dpapi:AQAAANCMnd8BFdERjHoAwE/Cl+sBAAAA")]
    [InlineData("dpapi:not base64!")]
    [InlineData("dpapi:")]
    [InlineData("plain:secret")]
    public void Dpapi_CorruptedOrOtherScheme_IsNull(string stored)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "DPAPI is Windows-only.");

        Assert.Null(new WindowsDpapiSecretProtector().Unprotect(stored));
    }

    [Fact]
    public void Dpapi_TamperedCiphertext_IsNull()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "DPAPI is Windows-only.");
        var dpapi = new WindowsDpapiSecretProtector();
        var bytes = Convert.FromBase64String((dpapi.Protect("secret") ?? "")["dpapi:".Length..]);
        bytes[^1] ^= 0xFF;

        Assert.Null(dpapi.Unprotect("dpapi:" + Convert.ToBase64String(bytes)));
    }
}
