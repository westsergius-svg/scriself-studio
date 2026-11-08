namespace ScriSelfStudio.EditorUI;

using AvaloniaRichEditor.Controls;
using ScriSelfStudio.Core.Text;

/// <summary>Просмотр снимка тем же адаптером, что используется основным редактором.</summary>
public sealed class SceneVersionPreview : RichEditor
{
    public SceneVersionPreview() => IsReadOnly = true;
    public void ShowDocument(RichDocument document) => Document = RichDocumentAdapter.ToFlowDocument(document, 16);
}
