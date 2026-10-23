using PzLauncher.Core.Platform;

namespace PzLauncher.Core.Tests.Platform;

public sealed class WindowsDetachedStartTests
{
    [Theory]
    [InlineData("-Xmx8g", "-Xmx8g")]
    [InlineData(@"C:\a\b", @"C:\a\b")]
    [InlineData("", "\"\"")]
    [InlineData("a b", "\"a b\"")]
    [InlineData(@"-cachedir=C:\My Server\", "\"-cachedir=C:\\My Server\\\\\"")]
    [InlineData("say \"hi\"", "\"say \\\"hi\\\"\"")]
    [InlineData("x\\\"y", "\"x\\\\\\\"y\"")]
    public void CommandLine_Argument_IsQuotedForTheMsvcrtParser(string argument, string expected) =>
        Assert.Equal(@"""C:\jre\java.exe"" -a " + expected, WindowsDetachedStart.CommandLine(@"C:\jre\java.exe", ["-a", argument]));
}
