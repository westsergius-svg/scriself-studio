namespace ScriSelfStudio.EditorEngine;

using ScriSelfStudio.Contracts.Errors;

/// <summary>
/// Буфер текста с единицами смещения UTF-16 (ТЗ п. 22). Все изменения проходят
/// через <see cref="Replace"/>, поэтому индекс и длина проверяются в одном месте.
/// </summary>
public sealed class TextBuffer
{
    private string _text;

    public TextBuffer(string? text = null) => _text = text ?? string.Empty;

    public string Text => _text;

    public int Length => _text.Length;

    /// <summary>Версия содержимого. Увеличивается при каждом изменении.</summary>
    public int Version { get; private set; }

    public Result Replace(int offset, int removeLength, string insertedText)
    {
        ArgumentNullException.ThrowIfNull(insertedText);

        var validation = ValidateRange(offset, removeLength);
        if (!validation.IsSuccess)
        {
            return validation;
        }

        if (removeLength == 0 && insertedText.Length == 0)
        {
            return Result.Success;
        }

        _text = string.Concat(
            _text.AsSpan(0, offset),
            insertedText.AsSpan(),
            _text.AsSpan(offset + removeLength));
        Version++;
        return Result.Success;
    }

    public Result Insert(int offset, string text) =>
        string.IsNullOrEmpty(text) ? Result.Success : Replace(offset, 0, text);

    public Result Delete(int offset, int length) =>
        length <= 0 ? Result.Success : Replace(offset, length, string.Empty);

    /// <summary>Фрагмент текста. Диапазон должен быть корректным.</summary>
    public string Slice(int offset, int length)
    {
        var validation = ValidateRange(offset, length);
        if (!validation.IsSuccess)
        {
            throw new ArgumentOutOfRangeException(nameof(offset), "Диапазон выходит за пределы текста.");
        }

        return _text.Substring(offset, length);
    }

    public Result ValidateRange(int offset, int length)
    {
        if (offset < 0 || offset > _text.Length)
        {
            return Result.FromError(ErrorCode.EditorOperationFailed, "Смещение выходит за пределы текста.");
        }

        if (length < 0 || offset + length > _text.Length)
        {
            return Result.FromError(ErrorCode.EditorOperationFailed, "Длина фрагмента выходит за пределы текста.");
        }

        return Result.Success;
    }
}
