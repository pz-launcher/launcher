using PzLauncher.Core.Common;
using PzLauncher.Core.Tests.TestSupport;
using PzLauncher.Core.ZombieBuddy;

namespace PzLauncher.Core.Tests.ZombieBuddy;

public sealed class ZabricModInfoTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly string _modFolder;

    public ZabricModInfoTests() => _modFolder = _temp.CreateDirectory("Zabric");

    public void Dispose() => _temp.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<ZabricModInfo> ReadAsync(string versionFolder)
    {
        var info = await ZabricModInfo.ReadAsync(versionFolder, Ct);
        Assert.NotNull(info);
        return info;
    }

    [Theory]
    [InlineData("id=Zabric", "Zabric")]
    [InlineData("ID=Zabric", "Zabric")]
    [InlineData("id=\nid= Zabric \nid=Other", "Zabric")]
    [InlineData("modid=Other\nid=Zabric", "Zabric")]
    [InlineData("name=Zabric", null)]
    public void Value_Lines_FirstNonEmptyValueOfTheKeyInAnyCase(string text, string? expected) =>
        Assert.Equal(expected, new ZabricModInfo("mod.info", "42", text.Split('\n')).Value("id"));

    [Fact]
    public async Task ReadAsync_NoModInfo_ReturnsNull() =>
        Assert.Null(await ZabricModInfo.ReadAsync(_temp.CreateDirectory("Zabric", "42"), Ct));

    [Fact]
    public async Task CheckPreloadableAsync_ShippedLayout_ReturnsAbsoluteInfAndJar()
    {
        var version = ZabricBuilder.Create(_modFolder);

        var entry = ResultAssert.Ok(await (await ReadAsync(version)).CheckPreloadableAsync(Ct));

        Assert.Equal(new ZabricPreloadEntry(ZabricBuilder.InfPath(version), ZabricBuilder.JarPath(version)), entry);
    }

    [Fact]
    public async Task CheckPreloadableAsync_VersionModInfoWithoutJarFile_UsesCommonModInfoWithTheJarInTheVersionFolder()
    {
        var version = ZabricBuilder.Create(_modFolder, modInfo: "id=Zabric\n");
        var common = _temp.CreateDirectory("Zabric", "common");
        await File.WriteAllTextAsync(ZabricBuilder.InfPath(common), ZabricBuilder.ModInfo(), Ct);

        var entry = ResultAssert.Ok(await (await ReadAsync(version)).CheckPreloadableAsync(Ct));

        Assert.Equal(new ZabricPreloadEntry(ZabricBuilder.InfPath(common), ZabricBuilder.JarPath(version)), entry);
    }

    [Theory]
    [InlineData("id=Zabric\njavaPkgName=com.pzlauncher.zabric\njavaPreload=true\n", ZabricModInfo.NoJarFile, false)]
    [InlineData("id=Zabric\njavaJarFile=media/java/Zabric.jar\njavaPreload=true\n", ZabricModInfo.MissingPkgName, false)]
    [InlineData("id=Zabric\njavaJarFile=media/java/Zabric.jar\njavaPkgName=com.pzlauncher.zabric\n", ZabricModInfo.NotJavaPreload, false)]
    [InlineData("id=Zabric\njavaJarFile=media/java/Other.jar\njavaPkgName=com.pzlauncher.zabric\njavaPreload=true\n", ZabricModInfo.MissingJar, true)]
    public async Task CheckPreloadableAsync_ModInfoFailsARequirement_ReturnsTheReason(string modInfo, string reason, bool jarPath)
    {
        var version = ZabricBuilder.Create(_modFolder, modInfo: modInfo);

        var error = ResultAssert.Failed(await (await ReadAsync(version)).CheckPreloadableAsync(Ct), ErrorCode.ZombieBuddyZabricNotPreloadable);

        Assert.Equal(reason, error.Details["reason"]);
        Assert.Equal(jarPath ? Path.Combine(version, "media", "java", "Other.jar") : ZabricBuilder.InfPath(version), error.Details["path"]);
    }

    [Theory]
    [InlineData("Manifest-Version: 1.0\r\n\r\n")]
    [InlineData(null)]
    public async Task CheckPreloadableAsync_ManifestWithoutPreload_ReturnsNoManifestPreload(string? manifest)
    {
        var version = ZabricBuilder.Create(_modFolder, manifest: manifest);

        var error = ResultAssert.Failed(await (await ReadAsync(version)).CheckPreloadableAsync(Ct), ErrorCode.ZombieBuddyZabricNotPreloadable);

        Assert.Equal((ZabricModInfo.NoManifestPreload, ZabricBuilder.JarPath(version)), (error.Details["reason"], error.Details["path"]));
    }

    [Fact]
    public async Task CheckPreloadableAsync_JarNotAZip_ReturnsUnreadableJar()
    {
        var version = ZabricBuilder.Create(_modFolder);
        await File.WriteAllTextAsync(ZabricBuilder.JarPath(version), "not a zip", Ct);

        var error = ResultAssert.Failed(await (await ReadAsync(version)).CheckPreloadableAsync(Ct), ErrorCode.ZombieBuddyZabricNotPreloadable);

        Assert.Equal(ZabricModInfo.UnreadableJar, error.Details["reason"]);
    }

    [Theory]
    [InlineData("Manifest-Version: 1.0\nZB-Preload: true\n", true)]
    [InlineData("Manifest-Version: 1.0\r\nzb-preload: TRUE \r\n\r\n", true)]
    [InlineData("ZB-Preload: tr\r\n ue\r\n", true)]
    [InlineData("ZB-Preload: true\nZB-Preload: false\n", false)]
    [InlineData("ZB-Preload:true\n", false)]
    [InlineData("Manifest-Version: 1.0\n\nName: a/B.class\nZB-Preload: true\n", false)]
    [InlineData("Manifest-Version: 1.0\nX-ZB-Preload: true\n", false)]
    public void HasManifestPreload_MainSection_ReadsLikeJavaManifest(string manifest, bool expected) =>
        Assert.Equal(expected, ZabricModInfo.HasManifestPreload(manifest));
}
