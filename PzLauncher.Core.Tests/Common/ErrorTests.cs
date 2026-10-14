using PzLauncher.Core.Common;

namespace PzLauncher.Core.Tests.Common;

public class ErrorTests
{
    [Fact]
    public void Details_NotSet_IsEmptyNotNull()
    {
        var error = new Error(ErrorCode.SteamNotInstalled, "msg", ErrorKind.NotFound);

        Assert.NotNull(error.Details);
        Assert.Empty(error.Details);
    }
}
