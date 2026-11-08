namespace ScriSelfStudio.EditorUI;

using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using ScriSelfStudio.Contracts.Editor;
using ScriSelfStudio.Core.Text;

internal sealed class ReviewEditorSession : IReviewEditorSession, IDisposable
{
    private readonly RichEditor _editor;
    private readonly Func<RichDocument, FlowDocument> _render;
    private readonly Action _synchronize;
    private bool _applying;

    public ReviewEditorSession(RichEditor editor, Func<RichDocument, FlowDocument> render, Action synchronize)
    {
        _editor = editor; _render = render; _synchronize = synchronize;
        editor.TextChanged += OnChanged;
        editor.SelectionChanged += OnChanged;
        editor.DocumentChanged += OnChanged;
    }

    public event EventHandler? Changed;
    public int CurrentSceneIndex => Locate(_editor.GetCaretPosition().Paragraph, _editor.GetCaretPosition().Offset)?.SceneIndex ?? 0;
    public ReviewSelection? Selection
    {
        get
        {
            var selection = _editor.GetHostSelection();
            var start = Locate(selection.Start, selection.StartOffset);
            var end = Locate(selection.End, selection.EndOffset);
            return start is not null && end is not null && start.SceneIndex == end.SceneIndex
                ? new(start.SceneIndex, start.Start, Math.Max(0, end.Start - start.Start)) : null;
        }
    }

    public IReadOnlyList<RichDocument> ReadScenes()
    {
        if (_editor.Document is not { } flow) return [];
        var ribbon = RichDocumentAdapter.FromFlowDocument(flow, _editor.DefaultFontSize);
        return RichDocumentAdapter.SplitRibbonDocument(ribbon, ribbon.Review.Sections?.Count ?? 1);
    }

    public void ApplyScene(int sceneIndex, RichDocument document)
    {
        var scenes = ReadScenes().ToList();
        if (sceneIndex < 0 || sceneIndex >= scenes.Count) throw new InvalidOperationException("Сцена больше не открыта.");
        ReviewEditing.Validate(document);
        scenes[sceneIndex] = document.Clone();
        _applying = true;
        try
        {
            _editor.ApplyHostDocument(_render(RichDocumentAdapter.ToRibbonDocument(scenes)));
            _synchronize();
        }
        finally { _applying = false; }
        UpdateHighlights();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SelectRange(int sceneIndex, int start, int length)
    {
        var first = PointerAt(sceneIndex, start);
        var last = PointerAt(sceneIndex, start + length);
        if (first is null || last is null) return;
        _editor.SelectHostRange(first.Paragraph!, first.Offset, last.Paragraph!, last.Offset);
        _editor.Focus();
    }

    private ReviewSelection? Locate(Paragraph? target, int local)
    {
        if (_editor.Document is null || target is null) return null;
        var index = 0;
        var offset = 0;
        foreach (var block in _editor.Document.Blocks)
        {
            if (block is DividerBlock) { index++; offset = 0; continue; }
            if (RichDocumentAdapter.IsSceneHeader(block)) continue;
            if (block is not Paragraph paragraph) continue;
            if (ReferenceEquals(target, paragraph)) return new(index, offset + local, 0);
            offset += RichEditor.GetParagraphText(paragraph).Length + 1;
        }
        return null;
    }

    private TextPointer? PointerAt(int sceneIndex, int offset)
    {
        if (_editor.Document is null) return null;
        var index = 0;
        foreach (var block in _editor.Document.Blocks)
        {
            if (block is DividerBlock) { index++; continue; }
            if (index != sceneIndex || RichDocumentAdapter.IsSceneHeader(block) || block is not Paragraph paragraph) continue;
            var length = RichEditor.GetParagraphText(paragraph).Length;
            if (offset <= length) return new(paragraph, Math.Max(0, offset));
            offset -= length + 1;
        }
        return null;
    }

    private void OnChanged(object? sender, EventArgs e)
    {
        if (_applying) return;
        UpdateHighlights();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateHighlights()
    {
        if (_editor.Document is null) return;
        try
        {
            var scenes = ReadScenes();
            var highlights = new List<ReviewHighlightSpan>();
            var index = 0;
            var offset = 0;
            foreach (var block in _editor.Document.Blocks)
            {
                if (block is DividerBlock) { index++; offset = 0; continue; }
                if (RichDocumentAdapter.IsSceneHeader(block) || block is not Paragraph paragraph || index >= scenes.Count) continue;
                var length = RichEditor.GetParagraphText(paragraph).Length;
                foreach (var c in scenes[index].Review.Comments.Where(c => !c.IsOrphaned && !c.IsResolved)) Add(c.Start, c.Length, true);
                foreach (var c in scenes[index].Review.Changes) Add(c.Start, c.Length, false);
                offset += length + 1;
                void Add(int start, int count, bool comment)
                {
                    var left = Math.Max(offset, start);
                    var right = Math.Min(offset + length, start + count);
                    if (right > left) highlights.Add(new(paragraph, left - offset, right - left, comment));
                }
            }
            _editor.SetReviewHighlights(highlights);
        }
        catch (InvalidDataException) { _editor.SetReviewHighlights([]); }
    }
    public void Dispose()
    {
        _editor.SetReviewHighlights([]);
        _editor.TextChanged -= OnChanged;
        _editor.SelectionChanged -= OnChanged;
        _editor.DocumentChanged -= OnChanged;
    }
}
