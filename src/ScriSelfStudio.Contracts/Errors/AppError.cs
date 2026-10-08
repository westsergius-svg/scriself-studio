namespace ScriSelfStudio.Contracts.Errors;

using System.Text.Json.Serialization;

/// <summary>
/// Типизированная ошибка приложения (ТЗ п. 26). <see cref="UserMessage"/> показывается
/// пользователю, <see cref="TechnicalDetails"/> пишется только в лог.
/// </summary>
public sealed record AppError
{
    public required ErrorCode Code { get; init; }

    public required string UserMessage { get; init; }

    public string? SuggestedAction { get; init; }

    public string? TechnicalDetails { get; init; }

    [JsonIgnore]
    public Exception? Exception { get; init; }

    public static AppError Create(
        ErrorCode code,
        string? userMessage = null,
        string? suggestedAction = null,
        string? technicalDetails = null,
        Exception? exception = null) =>
        new()
        {
            Code = code,
            UserMessage = userMessage ?? ErrorMessages.For(code),
            SuggestedAction = suggestedAction ?? ErrorMessages.SuggestedActionFor(code),
            TechnicalDetails = technicalDetails,
            Exception = exception,
        };
}
