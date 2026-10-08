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
    public void Apply(RichDocument document) =>
        DocumentEditor.ReplaceText(document, Offset, Removed.Length, Inserted);

    public void Revert(RichDocument document) => SnapshotChange.Replace(document, Before);
}

/// <summary>
/// Правка форматирования или стиля блоков. Структура спанов при этом меняется,
/// поэтому восстанавливается полным снимком — так история остаётся одной.
/// </summary>
internal sealed class SnapshotChange(RichDocument before, RichDocument after) : IDocumentChange
{
    private readonly RichDocument _before = before;
    private readonly RichDocument _after = after;

    public void Apply(RichDocument document) => Replace(document, _after);

    public void Revert(RichDocument document) => Replace(document, _before);

    internal static void Replace(RichDocument document, RichDocument source)
    {
        var copy = source.Clone();
        document.Blocks.Clear();
        document.Blocks.AddRange(copy.Blocks);
        document.SchemaVersion = copy.SchemaVersion;
    }
}
