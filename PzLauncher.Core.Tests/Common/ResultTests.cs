using PzLauncher.Core.Common;

namespace PzLauncher.Core.Tests.Common;

public class ResultTests
{
    private static readonly Error SampleError = new(ErrorCode.SteamNotInstalled, "sample", ErrorKind.NotFound);

    [Fact]
    public void Ok_AndImplicitFromValue_AreEqualSuccesses()
    {
        Result<string> converted = "v";

        Assert.Equal(Result<string>.Ok("v"), converted);
        Assert.True(converted.IsSuccess);
        Assert.Equal("v", converted.Value);
        Assert.Null(converted.Error);
    }

    [Fact]
    public void Fail_AndImplicitFromError_AreEqualFailures()
    {
        Result<string> converted = SampleError;

        Assert.Equal(Result<string>.Fail(SampleError), converted);
        Assert.False(converted.IsSuccess);
        Assert.Null(converted.Value);
        Assert.Same(SampleError, converted.Error);
    }

    [Fact]
    public void NonGeneric_OkAndImplicitFromError_SetSuccessAndError()
    {
        Result failed = SampleError;

        Assert.True(Result.Ok().IsSuccess);
        Assert.Null(Result.Ok().Error);
        Assert.False(failed.IsSuccess);
        Assert.Same(SampleError, failed.Error);
    }

    [Fact]
    public void Default_GenericAndNonGeneric_IsFailureWithUninitializedError()
    {
        var generic = default(Result<string>);
        var nonGeneric = default(Result);

        Assert.False(generic.IsSuccess);
        Assert.Null(generic.Value);
        Assert.Same(ResultErrors.Uninitialized, generic.Error);
        Assert.False(nonGeneric.IsSuccess);
        Assert.Same(ResultErrors.Uninitialized, nonGeneric.Error);
    }
}
