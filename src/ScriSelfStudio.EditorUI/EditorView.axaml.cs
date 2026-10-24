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

    public EditorView()
    {
        InitializeComponent();
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
        SyncRichEditorFromEngine();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!_syncingFromRichEditor
            && e.PropertyName is nameof(EditorViewModel.RibbonDocument)
                or nameof(EditorViewModel.EditorFontFamily)
                or nameof(EditorViewModel.EditorFontSize)
                or nameof(EditorViewModel.IsFullCanvas))
        {
            QueueRichEditorSync();
        }
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
            editor.Document = RichDocumentAdapter.ToRibbonFlowDocument(
                document,
                defaultFontSize,
                sceneHeaders);
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
        var paged = _viewModel?.IsFullCanvas == true;
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
