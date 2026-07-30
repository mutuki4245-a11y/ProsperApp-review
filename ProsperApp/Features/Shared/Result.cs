namespace ProsperApp.Features.Shared;

public enum ResultFailureKind
{
    InvalidInput,
    NotFound,
    Conflict,
    NotConfigured,
    PermissionDenied,
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

public sealed record PageLoadStatus(
    ResultFailureKind? FailureKind,
    string? ErrorMessage,
    DateTimeOffset? LastUpdatedAt)
{
    public bool Succeeded => FailureKind is null;

    public string FailureTitle => FailureKind switch
    {
        ResultFailureKind.NotConfigured => "設定が不足しています",
        ResultFailureKind.PermissionDenied => "権限を確認してください",
        ResultFailureKind.InvalidResponse => "取得結果を確認できません",
        _ => "情報を取得できません"
    };

    public static PageLoadStatus Success(DateTimeOffset updatedAt) =>
        new(null, null, updatedAt);

    public static PageLoadStatus Failure(ResultFailureKind kind, string message) =>
        new(kind, message, null);
}

public sealed record PageLoadIssue(
    string Area,
    ResultFailureKind FailureKind,
    string Message);
