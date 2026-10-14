using PzLauncher.Core.Common;

namespace PzLauncher.Core.Tests.Common;

public class ResultTests
{
    private static readonly Error SampleError = new(ErrorCode.SteamNotInstalled, "sample", ErrorKind.NotFound);

    [Fact]
    public void Ok_WithValue_IsSuccessAndHasValue()
    {
        var result = Result<int>.Ok(42);

        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
        Assert.Null(result.Error);
    }

    [Fact]
    public void Fail_WithError_IsFailureAndHasError()
    {
        var result = Result<string>.Fail(SampleError);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.Equal(ErrorCode.SteamNotInstalled, result.Error!.Code);
        Assert.Equal(ErrorKind.NotFound, result.Error.Kind);
    }

    [Fact]
    public void ImplicitConversion_FromValue_IsSuccess()
    {
        Result<string> result = "hello";

        Assert.True(result.IsSuccess);
        Assert.Equal("hello", result.Value);
    }

    [Fact]
    public void ImplicitConversion_FromError_IsFailure()
    {
        Result<string> result = SampleError;

        Assert.False(result.IsSuccess);
        Assert.Same(SampleError, result.Error);
    }

    [Fact]
    public void Ok_NullValue_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => Result<string>.Ok(null!));
    }

    [Fact]
    public void NonGeneric_Ok_IsSuccess()
    {
        var result = Result.Ok();

        Assert.True(result.IsSuccess);
        Assert.Null(result.Error);
    }

    [Fact]
    public void Default_Generic_IsFailureWithUninitializedError()
    {
        var result = default(Result<string>);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.Equal(ErrorCode.ResultUninitialized, result.Error.Code);
        Assert.Equal(ErrorKind.Unexpected, result.Error.Kind);
    }

    [Fact]
    public void New_Generic_IsFailureWithUninitializedError()
    {
        var result = new Result<int>();

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.ResultUninitialized, result.Error.Code);
    }

    [Fact]
    public void Default_NonGeneric_IsFailureWithUninitializedError()
    {
        var result = default(Result);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.ResultUninitialized, result.Error.Code);
        Assert.Equal(ErrorKind.Unexpected, result.Error.Kind);
    }

    [Fact]
    public void Equality_SameOkValue_AreEqual()
    {
        Assert.Equal(Result<int>.Ok(1), Result<int>.Ok(1));
        Assert.NotEqual(Result<int>.Ok(1), default);
    }

    [Fact]
    public void NonGeneric_FromError_IsFailure()
    {
        Result result = SampleError;

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.SteamNotInstalled, result.Error!.Code);
    }
}
