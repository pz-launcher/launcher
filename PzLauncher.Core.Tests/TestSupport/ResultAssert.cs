using PzLauncher.Core.Common;
using PzLauncher.Core.Installs;

namespace PzLauncher.Core.Tests.TestSupport;

public static class ResultAssert
{
    public static T Ok<T>(Result<T> result)
        where T : notnull
    {
        Assert.True(result.IsSuccess, result.Error?.Message);
        return result.Value;
    }

    public static Error Failed<T>(Result<T> result, ErrorCode code)
        where T : notnull
    {
        Assert.False(result.IsSuccess);
        Assert.Equal(code, result.Error.Code);
        return result.Error;
    }

    public static Error SingleWarning(InstallScanResult scan, ErrorCode code)
    {
        var warning = Assert.Single(scan.Warnings);
        Assert.Equal(code, warning.Code);
        return warning;
    }
}
