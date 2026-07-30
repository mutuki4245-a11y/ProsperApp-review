namespace ProsperApp.Features.Shared;

public enum ResultFailureKind
{
    InvalidInput,
    NotFound,
    Conflict,
    NotConfigured,
    Unavailable,
    InvalidResponse
}

public sealed record Result<T>
{
    private Result(bool succeeded, T value, ResultFailureKind? failureKind, string? errorMessage)
    {
        Succeeded = succeeded;
        Value = value;
        FailureKind = failureKind;
        ErrorMessage = errorMessage;
    }

    public bool Succeeded { get; }

    public T Value { get; }

    public ResultFailureKind? FailureKind { get; }

    public string? ErrorMessage { get; }

    public static Result<T> Success(T value) => new(true, value, null, null);

    public static Result<T> Failure(ResultFailureKind kind, string message) =>
        new(false, default!, kind, message);
}
