namespace ScriSelfStudio.EditorUI;

using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaRichEditor.Controls;
using AvaloniaRichEditor.Documents;
using ScriSelfStudio.Core.Text;

public partial class EditorView : UserControl
{
    private EditorViewModel? _viewModel;
    private bool _updatingRichEditor;
    private bool _syncingFromRichEditor;
    private bool _richEditorSyncQueued;
    private bool _resetUndoOnSync;
    private readonly IBrush? _toolbarBackground;
    private ProofreadingBehavior? _proofreading;
    private GrammarBehavior? _grammar;
    private ReviewEditorSession? _reviewSession;

    public EditorView()
    {
        InitializeComponent();
        _toolbarBackground = (RichEditorHost.Toolbar.Content as Border)?.Background;
        AttachedToVisualTree += (_, _) => { ApplyWorkspaceAppearance(); AttachProofreading(); AttachReview(); AttachGrammar(); };
        DetachedFromVisualTree += (_, _) => { _proofreading?.Dispose(); _proofreading = null; _grammar?.Dispose(); _grammar = null; DetachReview(); };
        _ = new DialogueInputBehavior(RichEditorHost.Editor);
        _ = new SentenceCapitalizationBehavior(RichEditorHost.Editor, () => _viewModel?.TextTools.AutoCapitalizeSentences == true);
        DataContextChanged += (_, _) => AttachViewModel(DataContext as EditorViewModel);
        RichEditorHost.Editor.TextChanged += OnRichEditorTextChanged;
        RichEditorHost.Editor.SelectionChanged += OnRichEditorSelectionChanged;
        RichEditorHost.Editor.AllowImages = false;
        RichEditorHost.Editor.AllowTables = false;
        RichEditorHost.Editor.AllowFindReplace = false;
        RichEditorHost.ShowStatusBar = false;
        RichEditorHost.ShowFileActions = false;
        RichEditorHost.Toolbar.ToolbarLevel = ToolbarLevel.Normal;
        RichEditorHost.Editor.Margin = new Thickness(0, 8, 0, 4);
    }

    private void AttachViewModel(EditorViewModel? viewModel)
    {
        _grammar?.Dispose();
        _grammar = null;
        DetachReview();
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = viewModel;
        if (_viewModel is null)
        {
            RichEditorHost.Editor.Document = new FlowDocument();
            return;
        }

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        UpdateReviewLayout();
        ApplyWorkspaceAppearance();
        ApplyToolbarLayout();
        SyncRichEditorFromEngine();
        ApplyReadingZoom();
        AttachProofreading();
        AttachReview();
        AttachGrammar();
    }

    private void AttachGrammar()
    {
        _grammar?.Dispose();
        _viewModel?.Grammar.RefreshPreferences();
        _grammar = _viewModel is null ? null : new GrammarBehavior(RichEditorHost.Editor, _viewModel.Grammar);
    }

    private void AttachProofreading()
    {
        _proofreading?.Dispose();
        _viewModel?.TextTools.RefreshPreferences();
        _proofreading = _viewModel is null ? null : new ProofreadingBehavior(RichEditorHost.Editor, _viewModel.TextTools);
    }

    private void OnReviewPanelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ReviewPanelViewModel.IsOpen)) UpdateReviewLayout();
    }

    private void UpdateReviewLayout() => EditorLayout.RowDefinitions[3].Height =
        new GridLength(_viewModel?.ReviewPanel.IsOpen == true ? 260 : 0);

    private void DetachReview()
    {
        if (_viewModel is not null) _viewModel.ReviewPanel.PropertyChanged -= OnReviewPanelPropertyChanged;
        _viewModel?.ReviewPanel.Attach(null);
        _reviewSession?.Dispose();
        _reviewSession = null;
    }

    private void AttachReview()
    {
        DetachReview();
        if (_viewModel is null) return;
        _viewModel.ReviewPanel.PropertyChanged += OnReviewPanelPropertyChanged;
        UpdateReviewLayout();
        _reviewSession = new ReviewEditorSession(RichEditorHost.Editor, CreateReviewFlow,
            () => OnRichEditorTextChanged(null, EventArgs.Empty));
        _viewModel.ReviewPanel.Attach(_reviewSession);
    }

    private AvaloniaRichEditor.Documents.FlowDocument CreateReviewFlow(ScriSelfStudio.Core.Text.RichDocument document)
    {
        var headers = _viewModel?.Blocks.Select((block, index) => new RichDocumentAdapter.RibbonSceneHeader(
            index, block.Title, block.StatusLabel, block.Words)).ToList();
        return RichDocumentAdapter.ToRibbonFlowDocument(document, RichEditorHost.Editor.DefaultFontSize, headers);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EditorViewModel.RestoredDocumentGeneration))
        {
            _resetUndoOnSync = true;
            QueueRichEditorSync();
        }
        if (e.PropertyName is nameof(EditorViewModel.IsStudioLayout) or nameof(EditorViewModel.IsFullCanvas)) ApplyWorkspaceAppearance();
        if (e.PropertyName is nameof(EditorViewModel.IsCabinetLayout) or nameof(EditorViewModel.ReadingZoom) or nameof(EditorViewModel.IsFullCanvas))
        {
            ApplyReadingZoom();
        }
        if (e.PropertyName is nameof(EditorViewModel.IsCabinetLayout) or nameof(EditorViewModel.IsToolbarMoreOpen))
        {
            ApplyToolbarLayout();
        }
        if (!_syncingFromRichEditor
            && e.PropertyName is nameof(EditorViewModel.RibbonDocument)
                or nameof(EditorViewModel.EditorFontFamily)
                or nameof(EditorViewModel.EditorFontSize)
                or nameof(EditorViewModel.IsFullCanvas))
        {
            QueueRichEditorSync();
        }
    }

    private void ApplyToolbarLayout()
    {
        RichEditorHost.Toolbar.ToolbarLevel = _viewModel is { IsCabinetLayout: true, IsToolbarMoreOpen: false }
            ? ToolbarLevel.Minimal : ToolbarLevel.Normal;
    }

    private void ApplyWorkspaceAppearance()
    {
        var studio = _viewModel?.IsStudioLayout == true;
        // Публичный Content штатной панели: меняется только поверхность, все команды
        // и сам документ остаются у компонента редактора.
        if (RichEditorHost.Toolbar.Content is Border border)
        {
            border.Background = studio && this.TryFindResource("PanelBrush", out var panel) && panel is IBrush brush
                ? brush : _toolbarBackground;
            border.Padding = studio ? new Thickness(12, 7) : new Thickness(8, 4);
        }
        RichEditorHost.Editor.Margin = studio ? new Thickness(12, 18, 12, 18) : new Thickness(0, 8, 0, 4);
        ApplyPageSetup();
        ApplyReadingZoom();
    }

    private void ApplyReadingZoom()
    {
        if (_viewModel is { IsStudioLayout: true })
            RichEditorHost.FitToWidth = true;
        else if (_viewModel is { IsCabinetLayout: true, IsFullCanvas: false })
            RichEditorHost.ZoomFactor = _viewModel.ReadingZoom;
        else
            RichEditorHost.FitToWidth = true;
    }

    private void QueueRichEditorSync()
    {
        if (_richEditorSyncQueued)
        {
            return;
        }

        _richEditorSyncQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _richEditorSyncQueued = false;
            if (!_syncingFromRichEditor)
            {
                SyncRichEditorFromEngine();
            }
        });
    }

    private void SyncRichEditorFromEngine()
    {
        if (_viewModel is null)
        {
            return;
        }

        var editor = RichEditorHost.Editor;
        editor.DefaultFontFamily = new FontFamily(_viewModel.EditorFontFamily);
        var defaultFontSize = _viewModel.EditorFontSize * 0.75;
        editor.DefaultFontSize = defaultFontSize;

        RichDocument document;
        if (_viewModel.IsSceneOpen)
        {
            try
            {
                document = _viewModel.RibbonDocument;
            }
            catch (InvalidDataException error)
            {
                _viewModel.ReportRichEditorError(error.Message);
                return;
            }
        }
        else
        {
            document = RichDocument.CreateEmpty();
        }

        try
        {
            _updatingRichEditor = true;
            var sceneHeaders = _viewModel.Blocks
                .Select(block => new RichDocumentAdapter.RibbonSceneHeader(
                    block.Order,
                    block.Title,
                    block.StatusLabel,
                    block.Words))
                .ToList();
            var flowDocument = RichDocumentAdapter.ToRibbonFlowDocument(
                document,
                defaultFontSize,
                sceneHeaders);
            if (_resetUndoOnSync)
            {
                editor.LoadDocument(flowDocument);
                _resetUndoOnSync = false;
            }
            else editor.Document = flowDocument;
            ApplyPageSetup();
            editor.MarkSaved();
        }
        catch (InvalidDataException error)
        {
            _viewModel.ReportRichEditorError(error.Message);
        }
        finally
        {
            _updatingRichEditor = false;
        }

        UpdateCaretSceneTitle();
    }

    /// <summary>
    /// Режим «Холст» показывает ленту листами формата Word (A4), вне режима — сплошной
    /// поток по ширине окна. Разметку листов рисует сам редактор, поэтому настройку нужно
    /// применять после каждой замены документа: лента пересобирается из модели.
    /// </summary>
    private void ApplyPageSetup()
    {
        var paged = _viewModel is { IsFullCanvas: true } or { IsStudioLayout: true };
        var editor = RichEditorHost.Editor;
        editor.PageSize = paged ? RichEditorPageSize.A4 : RichEditorPageSize.Continuous;
        editor.PageOrientation = RichEditorPageOrientation.Portrait;
        editor.ShowPageBoundaries = paged;
        editor.ShowPageNumbers = false;
        editor.PageHeader = null;
        editor.PageFooter = null;
    }

    private void OnRichEditorTextChanged(object? sender, EventArgs e)
    {
        if (_updatingRichEditor || _viewModel is null || RichEditorHost.Editor.Document is not { } document)
        {
            return;
        }

        var accepted = true;
        try
        {
            _syncingFromRichEditor = true;
            var model = RichDocumentAdapter.FromFlowDocument(
                document,
                RichEditorHost.Editor.DefaultFontSize);
            accepted = _viewModel.SynchronizeRibbonDocument(model);
        }
        catch (InvalidDataException error)
        {
            _viewModel.ReportRichEditorError(error.Message);
        }
        finally
        {
            _syncingFromRichEditor = false;
        }

        UpdateCaretSceneTitle();
        if (!accepted)
        {
            QueueRichEditorSync();
        }
    }

    private void OnRichEditorSelectionChanged(object? sender, EventArgs e) => UpdateCaretSceneTitle();

    private void UpdateCaretSceneTitle()
    {
        if (_updatingRichEditor || _viewModel is null)
        {
            return;
        }

        var status = RichEditorHost.Editor.GetStatus();
        _viewModel.SetRibbonCaretLine(status.Item3);
    }
}
