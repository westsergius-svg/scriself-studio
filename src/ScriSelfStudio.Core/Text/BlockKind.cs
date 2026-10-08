namespace ScriSelfStudio.Core.Text;

/// <summary>Тип блока канонической модели документа (ТЗ п. 11).</summary>
public enum BlockKind
{
    Paragraph = 0,
    Heading1 = 1,
    Heading2 = 2,
    Heading3 = 3,
    BulletListItem = 4,
    NumberedListItem = 5,
    Quote = 6,
    Separator = 7,
}
