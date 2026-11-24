namespace ShowingBooking.Domain;

public enum ErrorKind
{
    Validation, // -> HTTP 400
    NotFound,   // -> HTTP 404
    Conflict    // -> HTTP 409
}

/// <summary>An expected business failure. Code is stable and machine readable.</summary>
public sealed record Error(string Code, string Message, ErrorKind Kind);

/// <summary>Outcome of an operation that returns no value.</summary>
public sealed class Result
{
    private Result(Error? error) => Error = error;

    public Error? Error { get; }
    public bool IsSuccess => Error is null;

    public static Result Success() => new(null);
    public static Result Failure(Error error) => new(error);

    public static implicit operator Result(Error error) => Failure(error);
}

/// <summary>Outcome of an operation that returns a value, or an expected failure.</summary>
public sealed class Result<T>
{
    private readonly T? _value;

    private Result(T? value, Error? error)
    {
        _value = value;
        Error = error;
    }

    public Error? Error { get; }
    public bool IsSuccess => Error is null;

    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("A failed result has no value.");

    public static Result<T> Success(T value) => new(value, null);
    public static Result<T> Failure(Error error) => new(default, error);

    public static implicit operator Result<T>(T value) => Success(value);
    public static implicit operator Result<T>(Error error) => Failure(error);
}
