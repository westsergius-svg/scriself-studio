namespace ScriSelfStudio.EditorUI;

using ScriSelfStudio.Presentation.Localization;

using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;

/// <summary>Связывает словарные результаты с геометрией редактора, не изменяя документ.</summary>
public sealed partial class ProofreadingBehavior : IDisposable
{
    private readonly RichEditor _editor;
    private readonly TextToolsViewModel _model;
    private CancellationTokenSource? _pending;
    private bool _suppressCaretWord;
    private bool _disposed;

    public ProofreadingBehavior(RichEditor editor, TextToolsViewModel model)
    {
        _editor = editor;
        _model = model;
        editor.TextChanged += OnTextChanged;
        editor.DocumentChanged += OnDocumentChanged;
        editor.SelectionChanged += OnSelectionChanged;
        editor.LostFocus += OnLostFocus;
        editor.TextContextMenuOpening += OnContextMenu;
        editor.AddHandler(InputElement.TextInputEvent, OnTextInput, RoutingStrategies.Tunnel);
        editor.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        editor.AddHandler(InputElement.PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
        model.DictionaryChanged += OnDictionaryChanged;
        Schedule();
    }

    public IReadOnlyList<ProofreadingSpan> CurrentSpans { get; private set; } = [];

    private void OnTextInput(object? sender, TextInputEventArgs e) =>
        _suppressCaretWord = !string.IsNullOrEmpty(e.Text) && char.IsLetter(e.Text[^1]);

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        _suppressCaretWord = e.Key is Key.Back or Key.Delete;
        if (!_suppressCaretWord) Schedule();
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _suppressCaretWord = false;
        Schedule();
    }

    private void OnLostFocus(object? sender, RoutedEventArgs e)
    {
        _suppressCaretWord = false;
        Schedule();
    }

    private void OnTextChanged(object? sender, EventArgs e)
    {
        SetSpans([]);
        Schedule();
    }

    private void OnDocumentChanged(object? sender, EventArgs e)
    {
        _suppressCaretWord = false;
        OnTextChanged(sender, e);
    }

    private void OnSelectionChanged(object? sender, EventArgs e) => Schedule();

    private void OnDictionaryChanged(object? sender, EventArgs e)
    {
        SetSpans([]);
        Schedule();
    }

    private void Schedule() => _ = CheckAsync(TimeSpan.FromMilliseconds(650));

    public Task CheckNowAsync() => CheckAsync(TimeSpan.Zero);

    private async Task CheckAsync(TimeSpan delay)
    {
        _pending?.Cancel();
        _pending?.Dispose();
        using var request = new CancellationTokenSource();
        _pending = request;
        var token = request.Token;
        try
        {
            if (_disposed || !_model.IsEnabled)
            {
                SetSpans([]);
                return;
            }
            await Task.Delay(delay, token);
            var document = _editor.Document;
            var snapshots = (document?.Blocks.OfType<Paragraph>() ?? [])
                .Where(p => !RichDocumentAdapter.IsSceneHeader(p))
                .Select(p => (Paragraph: p, Text: RichEditor.GetParagraphText(p))).ToArray();
            var wholeText = string.Join('\n', snapshots.Select(s => s.Text));
            var known = _model.KnownWords();
            var issues = await _model.Service.CheckAsync(wholeText, token);
            if (token.IsCancellationRequested || _disposed || !ReferenceEquals(document, _editor.Document)) return;
            var caret = _editor.GetCaretPosition();
            var spans = new List<ProofreadingSpan>();
            var offset = 0;
            var index = 0;
            foreach (var snapshot in snapshots)
            {
                if (snapshot.Text != RichEditor.GetParagraphText(snapshot.Paragraph)) return;
                while (index < issues.Count && issues[index].Start < offset + snapshot.Text.Length)
                {
                    var issue = issues[index++];
                    var start = issue.Start - offset;
                    var active = _suppressCaretWord && _editor.IsFocused && ReferenceEquals(snapshot.Paragraph, caret.Paragraph)
                        && caret.Offset >= start && caret.Offset <= start + issue.Length;
                    if (!active && !known.Contains(issue.Word)) spans.Add(new(snapshot.Paragraph, start, issue.Word));
                }
                offset += snapshot.Text.Length + 1;
            }
            SetSpans(spans);
            if (spans.Count == 0) _model.Status = "Орфография: незнакомых слов нет";
            else _model.SetStatus("Орфография: незнакомых слов — {0}. Правой кнопкой — исправления.", spans.Count);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            if (!_disposed && !token.IsCancellationRequested)
            {
                SetSpans([]);
                _model.SetStatus("Проверка текста недоступна: {0}", error.Message);
            }
        }
        finally
        {
            if (ReferenceEquals(_pending, request)) _pending = null;
        }
    }

    private void SetSpans(IReadOnlyList<ProofreadingSpan> spans)
    {
        CurrentSpans = spans;
        _editor.SetProofreadingSpans(spans);
    }

    private void OnContextMenu(object? sender, TextContextMenuEventArgs e)
    {
        var paragraph = e.Position.Paragraph;
        if (paragraph is null || RichDocumentAdapter.IsSceneHeader(paragraph) || e.Menu.ItemsSource is not IList<Control> items) return;
        var text = RichEditor.GetParagraphText(paragraph);
        var word = WordTokens().Matches(text).Cast<Match>()
            .FirstOrDefault(m => e.Position.Offset >= m.Index && e.Position.Offset <= m.Index + m.Length);
        if (word is null) return;
        var span = new ProofreadingSpan(paragraph, word.Index, word.Value);
        var corrections = new ObservableCollection<MenuItem> { new() { Header = Loc.T("Ищем варианты…"), IsEnabled = false } };
        var synonyms = new ObservableCollection<MenuItem> { new() { Header = Loc.T("Ищем синонимы…"), IsEnabled = false } };
        items.Insert(0, new MenuItem { Header = Loc.T("Исправления"), ItemsSource = corrections });
        items.Insert(1, new MenuItem { Header = Loc.T("Синонимы · wiki-ru-wordnet"), ItemsSource = synonyms });
        var ignore = new MenuItem { Header = Loc.T("Пропустить слово до закрытия программы") };
        ignore.Click += (_, _) => _model.Ignore(word.Value);
        items.Insert(2, ignore);
        var personal = _model.IsPersonalWord(word.Value);
        var remember = new MenuItem { Header = personal ? Loc.T("Убрать из личного словаря") : Loc.T("Добавить в личный словарь") };
        remember.Click += async (_, _) => await _model.SetPersonalWordAsync(word.Value, !personal);
        items.Insert(3, remember);
        items.Insert(4, new Separator());
        _ = PopulateAsync(span, corrections, synonyms);
    }

    private async Task PopulateAsync(ProofreadingSpan span, ObservableCollection<MenuItem> corrections, ObservableCollection<MenuItem> synonyms)
    {
        try
        {
            var issues = await _model.Service.CheckAsync(span.Text);
            var unknown = issues.Count > 0 && !_model.KnownWords().Contains(span.Text);
            var suggestions = unknown ? await _model.Service.SuggestAsync(span.Text) : [];
            if (_disposed) return;
            Fill(corrections, suggestions, span, unknown ? Loc.T("Нет вариантов исправления") : Loc.T("Слово есть в словаре"));
            var words = await _model.Service.SynonymsAsync(span.Text);
            Fill(synonyms, words, span, Loc.T("Не найдено. Попробуйте начальную форму слова."));
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            corrections.Clear();
            synonyms.Clear();
            corrections.Add(new MenuItem { Header = Loc.T("Словарь недоступен"), IsEnabled = false });
            _model.SetStatus("Словарь недоступен: {0}", error.Message);
        }
    }

    private void Fill(ObservableCollection<MenuItem> items, IReadOnlyList<string> words, ProofreadingSpan span, string empty)
    {
        items.Clear();
        foreach (var word in words)
        {
            var replacement = char.IsUpper(span.Text[0]) && word.Length > 0 ? char.ToUpperInvariant(word[0]) + word[1..] : word;
            var item = new MenuItem { Header = replacement };
            item.Click += (_, _) =>
            {
                if (_disposed) return;
                if (!_editor.ReplaceProofreadingWord(span, replacement)) _model.Status = "Текст уже изменился. Откройте меню слова снова.";
                else _editor.Focus();
            };
            items.Add(item);
        }
        if (items.Count == 0) items.Add(new MenuItem { Header = empty, IsEnabled = false });
    }

    public void Dispose()
    {
        _disposed = true;
        _pending?.Cancel();
        _editor.TextChanged -= OnTextChanged;
        _editor.DocumentChanged -= OnDocumentChanged;
        _editor.SelectionChanged -= OnSelectionChanged;
        _editor.LostFocus -= OnLostFocus;
        _editor.TextContextMenuOpening -= OnContextMenu;
        _editor.RemoveHandler(InputElement.TextInputEvent, OnTextInput);
        _editor.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);
        _editor.RemoveHandler(InputElement.PointerPressedEvent, OnPointerPressed);
        _model.DictionaryChanged -= OnDictionaryChanged;
        SetSpans([]);
    }

    [GeneratedRegex("[A-Za-zА-Яа-яЁё]+(?:[-'’][A-Za-zА-Яа-яЁё]+)*", RegexOptions.CultureInvariant)]
    private static partial Regex WordTokens();
}
