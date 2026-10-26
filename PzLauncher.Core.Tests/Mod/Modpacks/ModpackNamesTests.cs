using PzLauncher.Core.Mod.Modpacks;

namespace PzLauncher.Core.Tests.Mod.Modpacks;

public sealed class ModpackNamesTests
{
    [Theory]
    [InlineData("Example Server", true)]
    [InlineData("  padded  ", true)]
    [InlineData("3012345678", true)]
    [InlineData("#items", true)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("two\nlines", false)]
    [InlineData("carriage\rreturn", false)]
    [InlineData("tab\tinside", false)]
    public void IsValid_ByBlankAndLines(string name, bool valid) =>
        Assert.Equal(valid, ModpackNames.IsValid(name));
}
