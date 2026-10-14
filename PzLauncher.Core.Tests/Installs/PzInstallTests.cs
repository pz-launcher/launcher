using PzLauncher.Core.Installs;

namespace PzLauncher.Core.Tests.Installs;

public class PzInstallTests
{
    [Fact]
    public void Source_WithSteamInfo_IsSteamLibrary()
    {
        var install = new PzInstall(PzInstallKind.Game, @"E:\Lib\ProjectZomboid", new SteamAppInfo("1", null));

        Assert.Equal(PzInstallSource.SteamLibrary, install.Source);
    }

    [Fact]
    public void Source_WithoutSteamInfo_IsManual()
    {
        var install = new PzInstall(PzInstallKind.Game, @"D:\PZ", Steam: null);

        Assert.Equal(PzInstallSource.Manual, install.Source);
    }
}
