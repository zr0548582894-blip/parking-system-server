namespace ParkingSystem.Core.Common;

// תוצאה של פעולה עסקית: הצלחה, או כישלון צפוי עם סוג והודעה.
// חריגות נשמרות לתקלות לא צפויות בלבד
public class Result
{
    protected Result(bool isSuccess, ErrorType? error, string? message)
    {
        IsSuccess = isSuccess;
        Error = error;
        Message = message;
    }

    public bool IsSuccess { get; }
    public ErrorType? Error { get; }
    public string? Message { get; }

    public static Result Success() => new(true, null, null);

    public static Result Fail(ErrorType error, string message) => new(false, error, message);
}

public class Result<T> : Result
{
    private Result(T? value, bool isSuccess, ErrorType? error, string? message)
        : base(isSuccess, error, message)
    {
        Value = value;
    }

    public T? Value { get; }

    public static Result<T> Success(T value) => new(value, true, null, null);

    public new static Result<T> Fail(ErrorType error, string message) => new(default, false, error, message);
}
