namespace ScriSelfStudio.Contracts.Editor;

using ScriSelfStudio.Contracts.Errors;
using ScriSelfStudio.Contracts.Projects;
using ScriSelfStudio.Core.Text;

/// <summary>
/// Единицы текстовых смещений контракта. Движок обязан объявлять единицы явно
/// и не смешивать их между операциями (ТЗ п. 22).
/// </summary>
public enum TextOffsetUnit
{
    /// <summary>Кодовые единицы UTF-16 от начала плоского текста документа.</summary>
    Utf16CodeUnit = 0,
}

/// <summary>Позиция в тексте документа в единицах <see cref="TextOffsetUnit.Utf16CodeUnit"/>.</summary>
public readonly record struct TextPosition(int Offset);

/// <summary>Диапазон текста [<see cref="Start"/>, <see cref="End"/>).</summary>
public readonly record struct TextRange(TextPosition Start, TextPosition End)
{
    public int Length => End.Offset - Start.Offset;

    public static TextRange Empty(TextPosition position) => new(position, position);
}

/// <summary>Параметры поиска по тексту сцены (ТЗ п. 6, 11).</summary>
public sealed record SearchOptions
{
    public static SearchOptions Default { get; } = new();

    public bool MatchCase { get; init; }

    public bool WholeWord { get; init; }
}

/// <summary>Найденное совпадение с коротким контекстом для панели поиска.</summary>
public sealed record SearchMatch
{
    public required TextRange Range { get; init; }

    public required string Preview { get; init; }
}

/// <summary>Результат поиска по документу сцены.</summary>
public sealed record SearchResult
{
    public required IReadOnlyList<SearchMatch> Matches { get; init; }

    public required int TotalCount { get; init; }
}

/// <summary>Статистика текста сцены (ТЗ п. 11).</summary>
public sealed record TextStatisticsDto
{
    public required int Characters { get; init; }

    public required int CharactersWithoutSpaces { get; init; }

    public required int Words { get; init; }

    public required int Paragraphs { get; init; }

    /// <summary>Число видимых символов с учётом составных символов и emoji.</summary>
    public required int GraphemeClusters { get; init; }
}

/// <summary>Стиль блока в позиции курсора: заголовок, список, выравнивание (ТЗ п. 11).</summary>
public sealed record BlockStyleDto
{
    public required BlockKind Kind { get; init; }

    public required TextAlignment Alignment { get; init; }

    public required int IndentLevel { get; init; }

    public ListMarkerKind ListMarker { get; init; }
}

/// <summary>
/// Оформление диапазона текста для панели форматирования: символьные флаги,
/// шрифт и цвета (ADR 003, шаг 3.1.5). Незаданные значения приходят как
/// <c>null</c> или пустая строка — это «смешанное» состояние выделения,
/// а не значение по умолчанию.
/// </summary>
public sealed record SpanStyleDto
{
    public required TextFormat Flags { get; init; }

    /// <summary>Семейство шрифта; <c>null</c> — не задано или выделение смешанное.</summary>
    public string? FontFamily { get; init; }

    /// <summary>Кегль; <c>null</c> — не задан или выделение смешанное.</summary>
    public double? FontSize { get; init; }

    /// <summary>Цвет текста в виде <c>#AARRGGBB</c>; пусто — из темы.</summary>
    public string Foreground { get; init; } = string.Empty;

    /// <summary>Цвет фона в виде <c>#AARRGGBB</c>; пусто — без подсветки.</summary>
    public string Highlight { get; init; } = string.Empty;
}

/// <summary>
/// Движок текста сцены. История отмены имеет одного владельца — движок (ТЗ п. 22).
/// Реализация — <c>ScriSelfStudio.EditorEngine.SceneEditorEngine</c>.
/// </summary>
public interface IEditorEngine
{
    Guid? SceneId { get; }

    /// <summary>Ревизия документа, от которой работает сессия.</summary>
    long BaseRevision { get; }

    bool IsDirty { get; }

    bool CanUndo { get; }

    bool CanRedo { get; }

    string PlainText { get; }

    /// <summary>Статистика текущего документа; для закрытой сессии — нули.</summary>
    TextStatisticsDto Statistics { get; }

    Task<Result<SceneDocumentDto>> OpenSceneAsync(Guid sceneId, CancellationToken cancellationToken = default);

    Task<Result> CloseAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Заменяет диапазон текста одной операцией — одна запись в истории отмены.
    /// Основной примитив адаптера текстового поля; вставка и удаление — его частные случаи.
    /// </summary>
    Result ReplaceRange(TextRange range, string text);

    Result InsertText(TextPosition position, string text);

    Result DeleteRange(TextRange range);

    /// <summary>
    /// Переключает флаги <paramref name="format"/> на диапазоне: если все символы уже
    /// имеют их, они снимаются, иначе устанавливаются.
    /// </summary>
    Result ApplyFormatting(TextRange range, TextFormat format);

    /// <summary>Формат, общий для всех символов диапазона (для состояния панели форматирования).</summary>
    Result<TextFormat> GetFormatting(TextRange range);

    /// <summary>Снимок текущего документа без коммита — например, для форматированного предпросмотра.</summary>
    Result<SceneDocumentDto> GetDocumentSnapshot();

    /// <summary>
    /// Задаёт оформление диапазона: шрифт, кегль и цвета. Меняются только
    /// переданные поля, символьные флаги сохраняются. Цвета — <c>#RRGGBB</c>
    /// или <c>#AARRGGBB</c>; <c>TextColor.None</c> снимает явный цвет (возврат
    /// к теме). Параметры <c>change*</c> отличают «не задавать поле» от
    /// «задать поле значением <c>null</c>» (ADR 003, шаг 3.1.5).
    /// </summary>
    Result ApplyStyle(
        TextRange range,
        string? fontFamily = null,
        bool changeFontFamily = false,
        double? fontSize = null,
        bool changeFontSize = false,
        TextColor? foreground = null,
        TextColor? highlight = null);

    /// <summary>Оформление диапазона — для панели шрифта и цвета.</summary>
    Result<SpanStyleDto> GetStyle(TextRange range);

    /// <summary>Стиль блока в позиции курсора.</summary>
    Result<BlockStyleDto> GetBlockStyleAt(TextPosition position);

    /// <summary>Меняет тип блока, выравнивание, отступ и маркер списка в диапазоне.</summary>
    Result ApplyBlockStyle(
        TextRange range,
        BlockKind? kind = null,
        TextAlignment? alignment = null,
        int? indentLevel = null,
        ListMarkerKind? listMarker = null);

    Result Undo();

    Result Redo();

    /// <summary>
    /// Заменяет содержимое открытой сцены плоским текстом: строки становятся
    /// абзацами. Операция не пишет в историю отмены движка: отмену ведёт сам
    /// элемент ввода, как в обычном текстовом поле Windows (ТЗ п. 11, 22).
    /// </summary>
    Result SetPlainText(string text);

    /// <summary>Replaces the document snapshot supplied by a rich-text editor.</summary>
    Result ReplaceDocument(RichDocument document);
    Result<SearchResult> Search(string query, SearchOptions? options = null);

    Result<UpdateSceneDocumentRequest> CreateCommitRequest();

    /// <summary>
    /// Передаёт текущий документ владельцу проекта и обновляет базовую ревизию.
    /// После успешного коммита <see cref="IsDirty"/> сбрасывается, история отмены сохраняется.
    /// </summary>
    Task<Result<SceneDocumentDto>> CommitAsync(CancellationToken cancellationToken = default);
}