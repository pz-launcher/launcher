using PzLauncher.Core.Common;
using PzLauncher.Core.Mod;
using PzLauncher.Core.Mod.ZombieBuddy;
using PzLauncher.Core.Tests.TestSupport;

namespace PzLauncher.Core.Tests.Mod.ZombieBuddy;

public sealed class ZombieBuddyJavaModTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly string _modFolder;

    public ZombieBuddyJavaModTests() => _modFolder = _temp.CreateDirectory("Zabric");

    public void Dispose() => _temp.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<Result<ZombieBuddyPreloadEntry>> CheckAsync(string versionFolder)
    {
        var info = await ModInfo.ReadAsync(versionFolder, Ct);
        Assert.NotNull(info);
        return await ZombieBuddyJavaMod.CheckPreloadableAsync("Zabric", info, Ct);
    }

    [Fact]
    public async Task CheckPreloadableAsync_ShippedLayout_ReturnsAbsoluteInfAndJar()
    {
        var version = ZabricBuilder.Create(_modFolder);

        var entry = ResultAssert.Ok(await CheckAsync(version));

        Assert.Equal(new ZombieBuddyPreloadEntry(ZabricBuilder.InfPath(version), ZabricBuilder.JarPath(version)), entry);
    }

    [Theory]
    [InlineData("id=Zabric\njavaPkgName=com.pzlauncher.zabric\njavaPreload=true\n", ZombieBuddyJavaMod.NoJarFile, false)]
    [InlineData("id=Zabric\njavaJarFile=media/java/Zabric.jar\njavaPreload=true\n", ZombieBuddyJavaMod.MissingPkgName, false)]
    [InlineData("id=Zabric\njavaJarFile=media/java/Zabric.jar\njavaPkgName=com.pzlauncher.zabric\n", ZombieBuddyJavaMod.NotJavaPreload, false)]
    [InlineData("id=Zabric\njavaJarFile=media/java/Other.jar\njavaPkgName=com.pzlauncher.zabric\njavaPreload=true\n", ZombieBuddyJavaMod.MissingJar, true)]
    public async Task CheckPreloadableAsync_ModInfoFailsARequirement_ReturnsTheReason(string modInfo, string reason, bool jarPath)
    {
        var version = ZabricBuilder.Create(_modFolder, modInfo: modInfo);

        var error = ResultAssert.Failed(await CheckAsync(version), ErrorCode.ZombieBuddyModNotPreloadable);

        Assert.Equal(reason, error.Details["reason"]);
        Assert.Equal("Zabric", error.Details["modId"]);
        Assert.Equal(jarPath ? Path.Combine(version, "media", "java", "Other.jar") : ZabricBuilder.InfPath(version), error.Details["path"]);
    }

    [Theory]
    [InlineData("Manifest-Version: 1.0\r\n\r\n")]
    [InlineData(null)]
    public async Task CheckPreloadableAsync_ManifestWithoutPreload_ReturnsNoManifestPreload(string? manifest)
    {
        var version = ZabricBuilder.Create(_modFolder, manifest: manifest);

        var error = ResultAssert.Failed(await CheckAsync(version), ErrorCode.ZombieBuddyModNotPreloadable);

        Assert.Equal((ZombieBuddyJavaMod.NoManifestPreload, ZabricBuilder.JarPath(version)), (error.Details["reason"], error.Details["path"]));
    }

    [Fact]
    public async Task CheckPreloadableAsync_JarNotAZip_ReturnsUnreadableJar()
    {
        var version = ZabricBuilder.Create(_modFolder);
        await File.WriteAllTextAsync(ZabricBuilder.JarPath(version), "not a zip", Ct);

        var error = ResultAssert.Failed(await CheckAsync(version), ErrorCode.ZombieBuddyModNotPreloadable);

        Assert.Equal(ZombieBuddyJavaMod.UnreadableJar, error.Details["reason"]);
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
        Assert.Equal(expected, ZombieBuddyJavaMod.HasManifestPreload(manifest));
}
