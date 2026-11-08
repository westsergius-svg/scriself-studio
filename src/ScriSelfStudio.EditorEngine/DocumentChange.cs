namespace ScriSelfStudio.EditorEngine;

using ScriSelfStudio.Core.Text;

/// <summary>
/// Одно изменение документа в единственной истории отмены (ТЗ п. 22).
/// </summary>
internal interface IDocumentChange
{
    void Apply(RichDocument document);

    void Revert(RichDocument document);
}

/// <summary>
/// Текстовая правка: смещение, удалённый фрагмент и вставленный текст. Повтор (redo)
/// выполняется той же операцией, откат — из снимка: удалённый диапазон мог содержать
/// несколько форматов, и по смещениям его нельзя восстановить без потери разметки.
/// </summary>
internal sealed record TextChange(int Offset, string Removed, string Inserted, RichDocument Before) : IDocumentChange
{
    private RichDocument? _after;

    public void Apply(RichDocument document)
    {
        if (_after is not null) { SnapshotChange.Replace(document, _after); return; }
        DocumentEditor.ReplaceText(document, Offset, Removed.Length, Inserted);
        ReviewEditing.Capture(Before, document, DateTimeOffset.UtcNow);
        _after = document.Clone();
    }

    public void Revert(RichDocument document) => SnapshotChange.Replace(document, Before);
}

/// <summary>
/// Правка форматирования или стиля блоков. Структура спанов при этом меняется,
/// поэтому восстанавливается полным снимком — так история остаётся одной.
/// </summary>
internal sealed class SnapshotChange(RichDocument before, RichDocument after) : IDocumentChange
{
    private readonly RichDocument _before = before;
    private readonly RichDocument _after = CaptureReview(before, after);

    private static RichDocument CaptureReview(RichDocument previous, RichDocument current)
    {
        ReviewEditing.Capture(previous, current, DateTimeOffset.UtcNow);
        return current;
    }

    public void Apply(RichDocument document) => Replace(document, _after);

    public void Revert(RichDocument document) => Replace(document, _before);

    internal static void Replace(RichDocument document, RichDocument source)
    {
        var copy = source.Clone();
        document.Blocks.Clear();
        document.Blocks.AddRange(copy.Blocks);
        document.SchemaVersion = copy.SchemaVersion;
        document.Review = copy.Review;
    }
}
