namespace ScriSelfStudio.EditorUI;

using ScriSelfStudio.Presentation.Localization;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ScriSelfStudio.Contracts.Projects;
using ScriSelfStudio.Core.Text;
using ScriSelfStudio.Presentation;

public sealed record SceneVersionItem(SceneVersionInfo Info)
{
    public string Name => Info.Name;
    public string Detail => $"{Info.CreatedAt.ToLocalTime():g} · {Loc.Count(Info.Words, "слово", "слова", "слов")}";
    public string Kind => Info.IsSafetySnapshot ? Loc.T("Сохранено перед восстановлением") : Loc.T("Именованная версия");
}

public sealed partial class SceneVersionsViewModel : ViewModelBase, IDisposable
{
    private readonly IProjectService _projects;
    private readonly Guid _sceneId;
    private long _revision;
    private string? _pendingAction;
    private int _comparisonGeneration;
    private bool _disposed;

    [ObservableProperty] private IReadOnlyList<SceneVersionItem> _versions = [];
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanUseSelectedVersion))]
    private SceneVersionItem? _selectedVersion;
    [ObservableProperty] private string _newVersionName = string.Empty;
    private string _status = string.Empty;
    public string Status
    {
        get => Loc.T(_status);
        set => SetProperty(ref _status, value);
    }
    [ObservableProperty] private string _comparisonSummary = Loc.T("Выберите версию для сравнения с текущей сценой.");
    [ObservableProperty] private SceneDiff _difference = new([], false);
    [ObservableProperty] private RichDocument _currentDocument = RichDocument.CreateEmpty();
    [ObservableProperty] private RichDocument _savedDocument = RichDocument.CreateEmpty();
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanChange))]
    [NotifyPropertyChangedFor(nameof(CanUseSelectedVersion))]
    private bool _isBusy;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanChange))]
    [NotifyPropertyChangedFor(nameof(CanUseSelectedVersion))]
    private bool _hasUnsavedChanges;
    [ObservableProperty] private bool _showConfirmation;
    [ObservableProperty] private string _confirmationText = string.Empty;

    public SceneVersionsViewModel(IProjectService projects, Guid sceneId)
    {
        _projects = projects;
        _sceneId = sceneId;
        SceneTitle = projects.Current?.Series.SelectMany(s => s.Books).SelectMany(b => b.Chapters)
            .SelectMany(c => c.Scenes).FirstOrDefault(s => s.Id == sceneId)?.Title ?? Loc.T("Сцена");
        Reload();
    }

    public string SceneTitle { get; }
    public bool CanChange => !IsBusy && !HasUnsavedChanges;
    public bool CanUseSelectedVersion => CanChange && SelectedVersion is not null;
    public Task PendingComparison { get; private set; } = Task.CompletedTask;

    partial void OnSelectedVersionChanged(SceneVersionItem? value)
    {
        CancelConfirmation();
        PendingComparison = CompareAsync();
    }

    private void Reload(Guid? selectId = null)
    {
        var current = _projects.GetSceneDocument(_sceneId);
        var versions = _projects.GetSceneVersions(_sceneId);
        if (!current.IsSuccess || !versions.IsSuccess)
        {
            Status = (current.Error ?? versions.Error)!.UserMessage;
            return;
        }
        _revision = current.Require().Revision;
        CurrentDocument = current.Require().Document;
        var selected = selectId ?? SelectedVersion?.Info.Id;
        Versions = versions.Require().Select(v => new SceneVersionItem(v)).ToArray();
        var previous = SelectedVersion;
        SelectedVersion = Versions.FirstOrDefault(v => v.Info.Id == selected) ?? Versions.FirstOrDefault();
        if (Equals(previous, SelectedVersion)) PendingComparison = CompareAsync();
    }

    private async Task CompareAsync()
    {
        var generation = ++_comparisonGeneration;
        if (SelectedVersion is null)
        {
            SavedDocument = RichDocument.CreateEmpty();
            Difference = new([], false);
            ComparisonSummary = Loc.T("Пока нет версий. Сохраните текущий текст перед переработкой сцены.");
            return;
        }
        var saved = _projects.GetSceneVersion(_sceneId, SelectedVersion.Info.Id);
        if (!saved.IsSuccess) { Status = saved.Error!.UserMessage; return; }
        SavedDocument = saved.Require().Document;
        var before = SavedDocument.ToPlainText();
        var after = CurrentDocument.ToPlainText();
        ComparisonSummary = Loc.T("Сравниваем…");
        var diff = await Task.Run(() => SceneTextDiff.Compare(before, after));
        if (_disposed || generation != _comparisonGeneration) return;
        Difference = diff;
        ComparisonSummary = before == after
            ? (EditorViewModel.AreDocumentsEquivalent(SavedDocument, CurrentDocument)
                ? Loc.T("Текст и оформление совпадают.")
                : EditorViewModel.AreDocumentsEquivalent(ReviewEditing.ContentOnly(SavedDocument), ReviewEditing.ContentOnly(CurrentDocument))
                    ? Loc.T("Текст и оформление совпадают; комментарии или правки отличаются.")
                    : Loc.T("Текст совпадает, оформление отличается. Сверьте обе вкладки."))
            : diff.IsCoarse ? Loc.T("Крупная переработка: изменения показаны целыми фрагментами.")
            : Loc.T("Зачёркнуто — удалено из сохранённой версии. Подчёркнуто — добавлено в текущую.");
    }

    [RelayCommand]
    private async Task CreateAsync()
    {
        if (!CanChange) return;
        var result = _projects.CreateSceneVersion(_sceneId, _revision, NewVersionName);
        if (!result.IsSuccess) { Status = result.Error!.UserMessage; return; }
        NewVersionName = string.Empty;
        HasUnsavedChanges = true;
        Reload(result.Require().Id);
        await SaveChangesAsync();
    }

    [RelayCommand]
    private void RequestRestore()
    {
        if (!CanChange || SelectedVersion is null) return;
        _pendingAction = "restore";
        ConfirmationText = Loc.F("Восстановить «{0}»? Текущий текст будет сохранён отдельной версией вместе с оформлением.", SelectedVersion.Name);
        ShowConfirmation = true;
    }

    [RelayCommand]
    private void RequestDelete()
    {
        if (!CanChange || SelectedVersion is null) return;
        _pendingAction = "delete";
        ConfirmationText = Loc.F("Удалить версию «{0}»? Текущий текст сцены останется прежним.", SelectedVersion.Name);
        ShowConfirmation = true;
    }

    [RelayCommand]
    private void CancelConfirmation() { _pendingAction = null; ShowConfirmation = false; }

    [RelayCommand]
    private async Task ConfirmAsync()
    {
        if (!CanChange || SelectedVersion is null || _pendingAction is null) return;
        var id = SelectedVersion.Info.Id;
        var restore = _pendingAction == "restore";
        CancelConfirmation();
        if (restore)
        {
            var result = _projects.RestoreSceneVersion(_sceneId, id, _revision);
            if (!result.IsSuccess) { Status = result.Error!.UserMessage; return; }
        }
        else
        {
            var result = _projects.DeleteSceneVersion(_sceneId, id);
            if (!result.IsSuccess) { Status = result.Error!.UserMessage; return; }
        }
        HasUnsavedChanges = true;
        Reload(restore ? id : null);
        await SaveChangesAsync();
        if (!HasUnsavedChanges && restore) Status = "Сцена восстановлена. Предыдущий текст сохранён в истории.";
    }

    [RelayCommand]
    private async Task SaveChangesAsync()
    {
        if (IsBusy || !HasUnsavedChanges) return;
        IsBusy = true;
        try
        {
            var saved = await _projects.SaveAsync();
            HasUnsavedChanges = !saved.IsSuccess;
            Status = saved.IsSuccess ? "Изменения сохранены в файле книги."
                : Loc.F("Изменения пока только в памяти. Повторите сохранение: {0}", Loc.T(saved.Error!.UserMessage));
        }
        finally { IsBusy = false; }
    }

    public void Dispose() { _disposed = true; _comparisonGeneration++; }
}
