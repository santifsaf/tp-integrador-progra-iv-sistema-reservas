namespace Availability.Application.Common;

public record Result<T>
{
    public bool IsSuccess { get; }

    public T? Value { get; }

    public AppError? Error { get; }

    private Result(bool isSuccess, T? value, AppError? error)
    {
        IsSuccess = isSuccess;
        Value = value;
        Error = error;
    }

    public static Result<T> Success(T value) => new(true, value, null);

    public static Result<T> Failure(AppError error) => new(false, default, error);
}
