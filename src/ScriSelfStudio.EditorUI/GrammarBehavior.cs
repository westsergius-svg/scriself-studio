namespace ScriSelfStudio.EditorUI;

using ScriSelfStudio.Presentation.Localization;

using Avalonia.Controls;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using ScriSelfStudio.Contracts.Editor;

public sealed record GrammarAnnotation(Paragraph Paragraph, string Snapshot, GrammarIssue Issue, long Generation);

/// <summary>Visual-only suggestions, invalidated on every text or document change.</summary>
public sealed class GrammarBehavior : IDisposable
{
    private readonly RichEditor _editor;
    private readonly GrammarViewModel _model;
    private readonly Dictionary<(string Language, string Text), IReadOnlyList<GrammarIssue>> _cache = [];
    private CancellationTokenSource? _pending;
    private FlowDocument? _checkedDocument;
    private bool _disposed;
    private long _generation;

    public GrammarBehavior(RichEditor editor, GrammarViewModel model)
    {
        _editor = editor; _model = model;
        editor.TextChanged += OnChanged;
        editor.DocumentChanged += OnDocumentChanged;
        editor.TextContextMenuOpening += OnContextMenu;
        model.PreferencesChanged += OnChanged;
        Schedule();
    }

    public IReadOnlyList<GrammarAnnotation> CurrentIssues { get; private set; } = [];
    public Task CheckNowAsync() => CheckAsync(TimeSpan.Zero);
    private void Schedule() => _ = CheckAsync(TimeSpan.FromMilliseconds(900));
    private void OnDocumentChanged(object? sender, EventArgs e) { _cache.Clear(); OnChanged(sender, e); }
    private void OnChanged(object? sender, EventArgs e)
    {
        _generation++;
        SetIssues([]);
        Schedule();
    }

    private async Task CheckAsync(TimeSpan delay)
    {
        _pending?.Cancel();
        using var request = new CancellationTokenSource();
        _pending = request;
        var token = request.Token;
        try
        {
            if (_disposed || !_model.IsEnabled) { SetIssues([]); return; }
            await Task.Delay(delay, token);
            var generation = _generation;
            var document = _editor.Document;
            var snapshots = (document?.Blocks.OfType<Paragraph>() ?? [])
                .Where(p => !RichDocumentAdapter.IsSceneHeader(p))
                .Select(p => (Paragraph: p, Text: RichEditor.GetParagraphText(p))).ToArray();
            var language = _model.Language;
            var annotations = new List<GrammarAnnotation>();
            _model.Status = "LanguageTool проверяет текст на компьютере…";
            foreach (var snapshot in snapshots)
            {
                if (string.IsNullOrWhiteSpace(snapshot.Text)) continue;
                if (!_cache.TryGetValue((language, snapshot.Text), out var issues))
                {
                    var response = await _model.Service.CheckAsync(snapshot.Text, language, token);
                    if (token.IsCancellationRequested || _disposed || generation != _generation || !ReferenceEquals(document, _editor.Document)) return;
                    if (!response.IsSuccess) { _model.Status = response.Error!.UserMessage; SetIssues([]); return; }
                    issues = response.Require();
                    if (_cache.Count >= 64) _cache.Clear();
                    _cache[(language, snapshot.Text)] = issues;
                }
                annotations.AddRange(issues.Select(i => new GrammarAnnotation(snapshot.Paragraph, snapshot.Text, i, generation)));
            }
            if (token.IsCancellationRequested || _disposed || generation != _generation || !ReferenceEquals(document, _editor.Document)
                || snapshots.Any(s => s.Text != RichEditor.GetParagraphText(s.Paragraph))) return;
            _checkedDocument = document;
            SetIssues(annotations);
            if (annotations.Count == 0) _model.Status = "LanguageTool: замечаний нет. Проверка запятых ограничена.";
            else _model.SetStatus("LanguageTool: замечаний — {0}. Правой кнопкой по синему подчёркиванию — варианты.", annotations.Count);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested || _disposed) { }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            if (!_disposed && !token.IsCancellationRequested)
            {
                SetIssues([]);
                _model.Status = "Локальная грамматика временно недоступна. Выключите и снова включите проверку.";
            }
        }
        finally { if (ReferenceEquals(_pending, request)) _pending = null; }
    }

    private void SetIssues(IReadOnlyList<GrammarAnnotation> issues)
    {
        CurrentIssues = issues;
        _editor.SetGrammarSpans(issues.Select(i => new GrammarSpan(i.Paragraph, i.Issue.Start, i.Issue.Length)));
    }

    public bool Apply(GrammarAnnotation annotation, string replacement)
    {
        if (_disposed || !_model.IsEnabled || annotation.Generation != _generation
            || !ReferenceEquals(_checkedDocument, _editor.Document)
            || RichEditor.GetParagraphText(annotation.Paragraph) != annotation.Snapshot
            || !annotation.Issue.Replacements.Contains(replacement, StringComparer.Ordinal))
        {
            if (!_disposed) _model.Status = "Текст изменился. Дождитесь новой проверки.";
            return false;
        }
        return _editor.ReplaceProofreadingWord(new(annotation.Paragraph, annotation.Issue.Start, annotation.Issue.OriginalText), replacement);
    }

    private void OnContextMenu(object? sender, TextContextMenuEventArgs e)
    {
        if (e.Menu.ItemsSource is not IList<Control> items) return;
        var matches = CurrentIssues.Where(i => ReferenceEquals(i.Paragraph, e.Position.Paragraph)
            && e.Position.Offset >= i.Issue.Start && e.Position.Offset <= i.Issue.Start + Math.Max(1, i.Issue.Length)).ToArray();
        if (matches.Length == 0) return;
        items.Insert(0, new Separator());
        foreach (var annotation in matches.Reverse())
        {
            var options = new List<MenuItem>();
            foreach (var replacement in annotation.Issue.Replacements)
            {
                var item = new MenuItem { Header = replacement.Length == 0 ? Loc.T("Удалить фрагмент") : replacement };
                item.Click += (_, _) => Apply(annotation, replacement);
                options.Add(item);
            }
            if (options.Count == 0) options.Add(new() { Header = Loc.T("Проверьте этот фрагмент вручную"), IsEnabled = false });
            items.Insert(0, new MenuItem
            {
                Header = new TextBlock { Text = Loc.F("Грамматика · {0}", annotation.Issue.Message), MaxWidth = 430, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                ItemsSource = options,
            });
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _pending?.Cancel();
        _editor.TextChanged -= OnChanged;
        _editor.DocumentChanged -= OnDocumentChanged;
        _editor.TextContextMenuOpening -= OnContextMenu;
        _model.PreferencesChanged -= OnChanged;
        _cache.Clear();
        SetIssues([]);
        _ = _model.StopAsync();
    }
}
