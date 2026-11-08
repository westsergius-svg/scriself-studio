namespace ScriSelfStudio.EditorUI;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ScriSelfStudio.Contracts.Editor;
using ScriSelfStudio.Core.Text;
using ScriSelfStudio.Presentation;
using ScriSelfStudio.Presentation.Localization;

public sealed record ReviewCommentItem(int SceneIndex, TextComment Comment)
{
    public string Text => Comment.Text;
    public string Quote => Comment.Quote;
    public string Detail => Loc.F("Сцена {0} · {1} · {2}", SceneIndex + 1, Comment.Author, Comment.CreatedAt.ToLocalTime().ToString("g"));
    public string State => Loc.T(Comment.IsOrphaned ? "Текст удалён" : Comment.IsResolved ? "Решён" : "Открыт");
}

public sealed record ReviewChangeItem(int SceneIndex, TrackedTextChange Change, string Removed, string Added)
{
    public string Detail => Loc.F("Сцена {0} · {1} · {2}", SceneIndex + 1, Change.Author, Change.CreatedAt.ToLocalTime().ToString("g"));
    public string Summary => Removed == Added ? Loc.T("Изменено оформление") : $"«{Short(Removed)}» → «{Short(Added)}»";
    private static string Short(string text) => text.Length > 70 ? text[..70] + "…" : text;
}

public sealed partial class ReviewPanelViewModel : ViewModelBase, IDisposable
{
    private IReviewEditorSession? _session;
    private string? _lastStoredAuthor;
    [ObservableProperty] private bool _isOpen;
    [ObservableProperty] private bool _isTracking;
    [ObservableProperty] private string _author = Loc.T("Автор");
    [ObservableProperty] private string _draft = string.Empty;
    private string _status = "Выделите текст и добавьте комментарий.";
    public string Status
    {
        get => Loc.T(_status);
        set => SetProperty(ref _status, value);
    }
    [ObservableProperty] private IReadOnlyList<ReviewCommentItem> _comments = [];
    [ObservableProperty] private IReadOnlyList<ReviewChangeItem> _changes = [];
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedComment))]
    [NotifyPropertyChangedFor(nameof(SelectedCommentQuote))]
    private ReviewCommentItem? _selectedComment;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedChangeRemoved))]
    [NotifyPropertyChangedFor(nameof(SelectedChangeAdded))]
    private ReviewChangeItem? _selectedChange;
    [ObservableProperty] private string _commentEditText = string.Empty;
    public string TrackingLabel => Loc.T(IsTracking ? "Запись правок включена" : "Включить запись правок");
    public bool HasSelectedComment => SelectedComment is not null;
    public string SelectedCommentQuote => SelectedComment?.Quote ?? string.Empty;
    public string SelectedChangeRemoved => SelectedChange?.Removed ?? Loc.T("Выберите правку");
    public string SelectedChangeAdded => SelectedChange?.Added ?? string.Empty;

    public void Attach(IReviewEditorSession? session)
    {
        if (_session is not null) _session.Changed -= OnChanged;
        _session = session;
        _lastStoredAuthor = null;
        if (_session is not null) _session.Changed += OnChanged;
        Refresh();
    }

    private void OnChanged(object? sender, EventArgs e) => Refresh();
    partial void OnSelectedCommentChanged(ReviewCommentItem? value) => CommentEditText = value?.Text ?? string.Empty;

    public void Refresh()
    {
        try
        {
            var scenes = _session?.ReadScenes() ?? [];
            var current = _session?.CurrentSceneIndex ?? 0;
            IsTracking = current < scenes.Count && scenes[current].Review.TrackChanges;
            if (current < scenes.Count && _lastStoredAuthor != scenes[current].Review.Author)
            {
                Author = scenes[current].Review.Author;
                _lastStoredAuthor = Author;
            }
            OnPropertyChanged(nameof(TrackingLabel));
            var commentId = SelectedComment?.Comment.Id;
            var changeId = SelectedChange?.Change.Id;
            Comments = scenes.SelectMany((s, i) => s.Review.Comments.Select(c => new ReviewCommentItem(i, c))).ToArray();
            Changes = scenes.SelectMany((s, i) => s.Review.Changes.OrderBy(c => c.Start)
                .Select(c => new ReviewChangeItem(i, c, ReviewEditing.RemovedText(c), ReviewEditing.AddedText(s, c)))).ToArray();
            // Keep an in-progress edit when the document refreshes but its comment did not change.
            var oldDraft = CommentEditText;
            var oldText = SelectedComment?.Text;
            SelectedComment = Comments.FirstOrDefault(c => c.Comment.Id == commentId);
            if (SelectedComment?.Text == oldText) CommentEditText = oldDraft;
            SelectedChange = Changes.FirstOrDefault(c => c.Change.Id == changeId);
        }
        catch (InvalidDataException error) { Status = error.Message; }
    }

    [RelayCommand] private void ToggleOpen() => IsOpen = !IsOpen;
    [RelayCommand] private void ToggleTracking() => Mutate(_session?.CurrentSceneIndex ?? 0,
        document => document.Review.TrackChanges = !document.Review.TrackChanges);
    [RelayCommand] private void ApplyAuthor() => Mutate(_session?.CurrentSceneIndex ?? 0, _ => { });

    [RelayCommand]
    private void AddComment()
    {
        var selection = _session?.Selection;
        if (selection is null || selection.Length == 0) { Status = "Выделите текст внутри одной сцены."; return; }
        if (Mutate(selection.SceneIndex, document => ReviewEditing.AddComment(document, selection.Start, selection.Length, Draft, DateTimeOffset.UtcNow)))
            Draft = string.Empty;
    }

    [RelayCommand] private void EditComment()
    {
        if (SelectedComment is not { } item) return;
        Mutate(item.SceneIndex, document =>
        {
            if (string.IsNullOrWhiteSpace(CommentEditText) || CommentEditText.Trim().Length > 10000)
                throw new ArgumentException("Комментарий должен содержать от 1 до 10000 символов.");
            document.Review.Comments.Single(c => c.Id == item.Comment.Id).Text = CommentEditText.Trim();
        });
    }
    [RelayCommand] private void ResolveComment()
    {
        if (SelectedComment is not { } item) return;
        Mutate(item.SceneIndex, d => { var c = d.Review.Comments.Single(c => c.Id == item.Comment.Id); c.IsResolved = !c.IsResolved; });
    }
    [RelayCommand] private void DeleteComment()
    {
        if (SelectedComment is { } item) Mutate(item.SceneIndex, d => d.Review.Comments.RemoveAll(c => c.Id == item.Comment.Id));
    }
    [RelayCommand] private void GoToComment()
    {
        if (SelectedComment is { Comment.IsOrphaned: false } item) _session?.SelectRange(item.SceneIndex, item.Comment.Start, item.Comment.Length);
    }
    [RelayCommand] private void AcceptChange()
    {
        if (SelectedChange is { } item) Mutate(item.SceneIndex, d => ReviewEditing.Accept(d, item.Change.Id));
    }
    [RelayCommand] private void RejectChange()
    {
        if (SelectedChange is { } item) Mutate(item.SceneIndex, d => ReviewEditing.Reject(d, item.Change.Id));
    }

    private bool Mutate(int index, Action<RichDocument> action)
    {
        if (_session is null) return false;
        try
        {
            var scenes = _session.ReadScenes();
            if (index >= scenes.Count) return false;
            var document = scenes[index];
            document.Review.Author = string.IsNullOrWhiteSpace(Author) ? Loc.T("Автор") : Author.Trim()[..Math.Min(Author.Trim().Length, 120)];
            action(document);
            _session.ApplyScene(index, document);
            Status = "Изменения внесены. Ctrl+S — сохранить книгу; Ctrl+Z в тексте — отменить.";
            return true;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or InvalidDataException)
        { Status = error.Message; return false; }
    }

    public void RefreshLocalization()
    {
        Refresh();
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(SelectedChangeRemoved));
    }

    public void Dispose() => Attach(null);
}
