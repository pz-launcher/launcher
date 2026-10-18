using System.Text.Json;
using PzLauncher.Core.Serialization;
using PzLauncher.Core.Update;
using static PzLauncher.Core.Tests.TestSupport.UpdateTestKit;

namespace PzLauncher.Core.Tests.Update;

public class SemVersionTests
{
    [Theory]
    [InlineData("0.2.0", 0, 2, 0, "")]
    [InlineData("1.2.3-beta.1", 1, 2, 3, "beta.1")]
    [InlineData("1.2.3+abc123", 1, 2, 3, "")]
    [InlineData("10.20.30-rc.1+build.05", 10, 20, 30, "rc.1")]
    [InlineData("1.0.0-x-y-z.--", 1, 0, 0, "x-y-z.--")]
    public void TryParse_Valid_ReadsCoreAndPrerelease(string text, int major, int minor, int patch, string prerelease)
    {
        Assert.True(SemVersion.TryParse(text, out var version));
        Assert.Equal(new SemVersion(major, minor, patch, prerelease), version);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1.2")]
    [InlineData("1.2.3.4")]
    [InlineData("v1.2.3")]
    [InlineData("01.2.3")]
    [InlineData("1.2.x")]
    [InlineData("1.2.3-")]
    [InlineData("1.2.3-01")]
    [InlineData("1.2.3-a..b")]
    [InlineData("1.2.3+")]
    [InlineData("1.2.3-a_b")]
    [InlineData("99999999999.0.0")]
    public void TryParse_Invalid_ReturnsFalse(string? text) =>
        Assert.False(SemVersion.TryParse(text, out _));

    [Fact]
    public void CompareTo_SpecExamples_AreInPrecedenceOrder()
    {
        string[] ordered =
        [
            "0.9.0", "0.10.0", "1.0.0-alpha", "1.0.0-alpha.1", "1.0.0-alpha.beta", "1.0.0-beta", "1.0.0-beta.2",
            "1.0.0-beta.11", "1.0.0-rc.1", "1.0.0", "1.0.1", "1.1.0", "2.0.0",
        ];

        for (var i = 1; i < ordered.Length; i++)
        {
            Assert.True(V(ordered[i - 1]) < V(ordered[i]), $"{ordered[i - 1]} < {ordered[i]}");
            Assert.True(V(ordered[i]) > V(ordered[i - 1]), $"{ordered[i]} > {ordered[i - 1]}");
        }
    }

    [Fact]
    public void Equality_IgnoresBuildMetadata()
    {
        Assert.Equal(V("1.0.0+a"), V("1.0.0+b"));
        Assert.Equal(0, V("1.0.0+a").CompareTo(V("1.0.0")));
        Assert.True(V("1.0.0") >= V("1.0.0+x"));
        Assert.True(V("1.0.0") <= V("1.0.0+x"));
    }

    [Theory]
    [InlineData("0.2.0+e4e3af3eab41f80788de0c6e5e93dc213d037eae", "0.2.0", false)]
    [InlineData("0.0.0-dev", "0.0.0-dev", true)]
    [InlineData("0.0.0-ci.42+sha", "0.0.0-ci.42", true)]
    [InlineData("0.0.1-dev", "0.0.1-dev", false)]
    [InlineData("0.0.0", "0.0.0", false)]
    [InlineData(null, "0.0.0-dev", true)]
    [InlineData("1.0.0.0", "0.0.0-dev", true)]
    public void FromInformationalVersion_ParsesOrFallsBackToDevelopment(string? informational, string expected, bool isDevelopment)
    {
        var version = SemVersion.FromInformationalVersion(informational);

        Assert.Equal(expected, version.ToString());
        Assert.Equal(isDevelopment, version.IsDevelopment);
    }

    [Fact]
    public void Json_IsTheVersionString()
    {
        var json = JsonSerializer.Serialize(V("1.2.3-rc.1"), CoreJsonContext.Default.SemVersion);

        Assert.Equal("\"1.2.3-rc.1\"", json);
        Assert.Equal(V("1.2.3-rc.1"), JsonSerializer.Deserialize(json, CoreJsonContext.Default.SemVersion));
    }

    [Theory]
    [InlineData("\"1.2\"")]
    [InlineData("123")]
    [InlineData("{\"major\":1}")]
    public void Json_NotAVersionString_Throws(string json) =>
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(json, CoreJsonContext.Default.SemVersion));
}
