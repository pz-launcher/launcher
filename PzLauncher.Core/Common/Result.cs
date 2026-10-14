using System.Diagnostics.CodeAnalysis;

namespace PzLauncher.Core.Common;

/// <summary><c>default(Result)</c> is a failure (<see cref="ResultErrors.Uninitialized"/>), never a success.</summary>
public readonly record struct Result
{
    private readonly bool _isSuccess;
    private readonly Error? _error;

    private Result(bool isSuccess, Error? error)
    {
        _isSuccess = isSuccess;
        _error = error;
    }

    [MemberNotNullWhen(false, nameof(Error))]
    public bool IsSuccess => _isSuccess;

    public Error? Error => _isSuccess ? null : _error ?? ResultErrors.Uninitialized;

    public static Result Ok() => new(true, null);

    public static Result Fail(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new Result(false, error);
    }

    public static implicit operator Result(Error error) => Fail(error);
}

/// <summary><c>default(Result&lt;T&gt;)</c> is a failure (<see cref="ResultErrors.Uninitialized"/>), never a success with a null value.</summary>
public readonly record struct Result<T>
{
    private readonly bool _isSuccess;
    private readonly Error? _error;

    private Result(bool isSuccess, T? value, Error? error)
    {
        _isSuccess = isSuccess;
        Value = value;
        _error = error;
    }

    [MemberNotNullWhen(true, nameof(Value))]
    [MemberNotNullWhen(false, nameof(Error))]
    public bool IsSuccess => _isSuccess;

    public T? Value { get; }

    public Error? Error => _isSuccess ? null : _error ?? ResultErrors.Uninitialized;

    public static Result<T> Ok(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new Result<T>(true, value, null);
    }

    public static Result<T> Fail(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new Result<T>(false, default, error);
    }

    public static implicit operator Result<T>(T value) => Ok(value);

    public static implicit operator Result<T>(Error error) => Fail(error);
}

public static class ResultErrors
{
    public static readonly Error Uninitialized =
        new(ErrorCode.ResultUninitialized, "Result was default-constructed instead of created with Ok/Fail.", ErrorKind.Unexpected);
}
