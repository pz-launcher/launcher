using PzLauncher.Core.Mod;
using PzLauncher.Core.Tests.TestSupport;

namespace PzLauncher.Core.Tests.Mod;

public sealed class ModInfoTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly string _modFolder;

    public ModInfoTests() => _modFolder = _temp.CreateDirectory("Zabric");

    public void Dispose() => _temp.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("id=Zabric", "Zabric")]
    [InlineData("ID=Zabric", "Zabric")]
    [InlineData("id=\nid= Zabric \nid=Other", "Zabric")]
    [InlineData("modid=Other\nid=Zabric", "Zabric")]
    [InlineData("name=Zabric", null)]
    public void Value_Lines_FirstNonEmptyValueOfTheKeyInAnyCase(string text, string? expected) =>
        Assert.Equal(expected, new ModInfo("mod.info", "42", text.Split('\n')).Value("id"));

    [Fact]
    public async Task ReadAsync_NoModInfo_ReturnsNull() =>
        Assert.Null(await ModInfo.ReadAsync(_temp.CreateDirectory("Zabric", "42"), Ct));

    [Fact]
    public async Task ReadAsync_ShippedLayout_ReadsTheVersionFoldersModInfo()
    {
        var version = ZabricBuilder.Create(_modFolder);

        var info = await ModInfo.ReadAsync(version, Ct);

        Assert.NotNull(info);
        Assert.Equal((ZabricBuilder.InfPath(version), version), (info.InfPath, info.VersionFolder));
    }

    [Fact]
    public async Task ReadAsync_VersionModInfoWithoutJarFile_UsesCommonModInfoForTheVersionFolder()
    {
        var version = ZabricBuilder.Create(_modFolder, modInfo: "id=Zabric\n");
        var common = _temp.CreateDirectory("Zabric", "common");
        await File.WriteAllTextAsync(ZabricBuilder.InfPath(common), ZabricBuilder.ModInfo(), Ct);

        var info = await ModInfo.ReadAsync(version, Ct);

        Assert.NotNull(info);
        Assert.Equal((ZabricBuilder.InfPath(common), version), (info.InfPath, info.VersionFolder));
    }
}
