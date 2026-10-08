namespace ScriSelfStudio.Contracts.Errors;

using System.Diagnostics.CodeAnalysis;

/// <summary>Результат операции без полезной нагрузки (ТЗ п. 24).</summary>
public sealed class Result
{
    private Result(AppError? error) => Error = error;

    public static Result Success { get; } = new(null);

    public bool IsSuccess => Error is null;

    public AppError? Error { get; }

    public static Result Failure(AppError error) => new(error);

    public static Result FromError(ErrorCode code, string? userMessage = null, string? technicalDetails = null, Exception? exception = null) =>
        Failure(AppError.Create(code, userMessage, technicalDetails: technicalDetails, exception: exception));
}

/// <summary>Результат операции с полезной нагрузкой (ТЗ п. 24).</summary>
public sealed class Result<T>
{
    private Result(T? value, AppError? error)
    {
        Value = value;
        Error = error;
    }

    public bool IsSuccess => Error is null;

    public T? Value { get; }

    public AppError? Error { get; }

    public static Result<T> Success(T value) => new(value, null);

    public static Result<T> Failure(AppError error) => new(default, error);

    public static Result<T> FromError(ErrorCode code, string? userMessage = null, string? technicalDetails = null, Exception? exception = null) =>
        Failure(AppError.Create(code, userMessage, technicalDetails: technicalDetails, exception: exception));

    public bool TryGetValue([MaybeNullWhen(false)] out T value)
    {
        value = Value;
        return IsSuccess;
    }

    /// <summary>Значение результата. Бросает исключение, если операция завершилась ошибкой.</summary>
    public T Require() => IsSuccess
        ? Value!
        : throw new InvalidOperationException($"Операция завершилась ошибкой {Error!.Code}.");
}
