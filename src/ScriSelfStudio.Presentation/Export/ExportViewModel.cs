namespace ScriSelfStudio.Presentation.Export;

using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ScriSelfStudio.Contracts.Books;
using ScriSelfStudio.Contracts.Dialogs;
using ScriSelfStudio.Contracts.Navigation;
using ScriSelfStudio.Contracts.Projects;
using ScriSelfStudio.Core.Model;
using ScriSelfStudio.Core.Text;
using ScriSelfStudio.Presentation;
using ScriSelfStudio.Presentation.Localization;

public sealed record ExportFormatOption(string Key, string Extension, string Name, string Description, string IconGlyph);

public sealed record ExportRangeOption(string Key, string Title);

public sealed partial class ExportViewModel : ViewModelBase, IDisposable
{
    private readonly IBookArchitectureService _architecture;
    private readonly IProjectService _projects;
    private readonly INavigationService _navigation;
    private readonly IFileDialogService _dialogs;
    private bool _refreshingTranslations;
    private ExportManuscript? _previewManuscript;

    [ObservableProperty]
    private ExportFormatOption _selectedFormat = null!;

    [ObservableProperty]
    private ExportRangeOption _selectedRange = null!;

    [ObservableProperty]
    private string _selectedDocumentStyle = Loc.T("Классический (книжный)");

    [ObservableProperty]
    private string _selectedFont = "Georgia";

    [ObservableProperty]
    private string _selectedFontSize = "12";

    [ObservableProperty]
    private string _selectedLineSpacing = "1.5";

    [ObservableProperty]
    private bool _includeTitlePage = true;

    [ObservableProperty]
    private bool _pageNumbering = true;

    [ObservableProperty]
    private bool _tableOfContents = true;

    [ObservableProperty]
    private bool _footnotes = true;

    [ObservableProperty]
    private bool _preserveFormatting = true;

    [ObservableProperty]
    private bool _includeSceneHeadings;

    [ObservableProperty]
    private bool _includeCharacterCards;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    private string? _statusMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _errorMessage;

    [ObservableProperty]
    private IReadOnlyList<ExportPreviewBlock> _previewBlocks = [];

    [ObservableProperty]
    private string _previewPageCounter = string.Empty;

    public ExportViewModel(
        IBookArchitectureService architecture,
        IProjectService projects,
        INavigationService navigation,
        IFileDialogService dialogs)
    {
        _architecture = architecture;
        _projects = projects;
        _navigation = navigation;
        _dialogs = dialogs;

        RebuildTranslatedOptions();
        Fonts = ["Georgia", "Inter", "Times New Roman", "Segoe UI", "Consolas"];
        FontSizes = ["10", "11", "12", "14", "16"];
        LineSpacings = ["1.0", "1.15", "1.5", "2.0"];

        _selectedFormat = Formats.Single(format => format.Key == "TXT");
        _selectedRange = Ranges[0];

        _navigation.Navigated += OnNavigated;
        LocalizationManager.Instance.PropertyChanged += OnLanguageChanged;
        UpdatePreview();
    }

    public IReadOnlyList<ExportFormatOption> Formats { get; private set; } = [];

    public IReadOnlyList<ExportRangeOption> Ranges { get; private set; } = [];

    public IReadOnlyList<string> DocumentStyles { get; private set; } = [];

    public IReadOnlyList<string> Fonts { get; }

    public IReadOnlyList<string> FontSizes { get; }

    public IReadOnlyList<string> LineSpacings { get; }

    public bool HasStatus => !string.IsNullOrEmpty(StatusMessage);

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public bool HasProject => _projects.Current is not null;

    public string ProjectTitle => _projects.Current?.Name ?? string.Empty;

    public string SettingsHeader => Loc.F("Настройки экспорта ({0})", SelectedFormat?.Name ?? string.Empty);

    partial void OnSelectedFormatChanged(ExportFormatOption value) =>
        OnPropertyChanged(nameof(SettingsHeader));

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (_refreshingTranslations || _previewManuscript is null) return;
        if (e.PropertyName == nameof(PreserveFormatting))
        {
            UpdatePreview();
        }
        else if (e.PropertyName is nameof(SelectedFormat) or nameof(SelectedFont) or nameof(SelectedFontSize)
            or nameof(SelectedLineSpacing) or nameof(IncludeTitlePage) or nameof(TableOfContents)
            or nameof(PageNumbering) or nameof(IncludeSceneHeadings) or nameof(IncludeCharacterCards))
        {
            RefreshPreview();
        }
    }

    /// <summary>
    /// Стиль документа задаёт согласованный набор шрифта, размера и интервала.
    /// Дальше автор может поправить любой из этих параметров отдельно.
    /// </summary>
    partial void OnSelectedDocumentStyleChanged(string value)
    {
        if (_refreshingTranslations) return;

        if (value == Loc.T("Классический (книжный)"))
        {
            SelectedFont = "Georgia";
            SelectedFontSize = "12";
            SelectedLineSpacing = "1.5";
        }
        else if (value == Loc.T("Стандартный"))
        {
            SelectedFont = "Times New Roman";
            SelectedFontSize = "12";
            SelectedLineSpacing = "1.15";
        }
        else if (value == Loc.T("Современный"))
        {
            SelectedFont = "Inter";
            SelectedFontSize = "11";
            SelectedLineSpacing = "1.5";
        }
        else if (value == Loc.T("Академический"))
        {
            SelectedFont = "Times New Roman";
            SelectedFontSize = "14";
            SelectedLineSpacing = "2.0";
        }
    }

    private void OnNavigated(object? sender, AppScreen screen)
    {
        if (screen == AppScreen.Export)
        {
            UpdatePreview();
        }
    }

    public void UpdatePreview()
    {
        if (_projects.Current is null)
        {
            _previewManuscript = null;
            PreviewBlocks = [new([new(Loc.T("Откройте проект"))], "Georgia", 16, 24)];
            PreviewPageCounter = string.Empty;
            return;
        }
        _previewManuscript = BuildManuscript();
        RefreshPreview();
    }

    private void RefreshPreview()
    {
        if (_previewManuscript is null) return;
        PreviewBlocks = ExportPreviewBuilder.Build(_previewManuscript, BuildOptions(), SelectedFormat.Key);
        PreviewPageCounter = Loc.T(SelectedFormat.Key is "TXT" or "Markdown"
            ? "Точное содержимое текстового файла."
            : "Предпросмотр содержания и оформления. Разбиение на страницы зависит от формата.");
    }

    private ExportManuscript BuildManuscript() => ManuscriptExportBuilder.Build(
        ProjectTitle, _architecture.GetBooks(), ReadSceneDocument, PreserveFormatting, _architecture.GetCharacters());

    [RelayCommand]
    private async Task ExportAsync(CancellationToken cancellationToken = default)
    {
        StatusMessage = null;
        ErrorMessage = null;

        var format = SelectedFormat;
        if (!HasProject)
        {
            ErrorMessage = Loc.T("Сначала откройте проект.");
            return;
        }
        if (SelectedRange.Key != "All")
        {
            ErrorMessage = Loc.T("Пока поддерживается экспорт всего проекта.");
            return;
        }
        var fileName = $"{ProjectTitle}_{format.Key}".Replace(" ", "_");
        var filePath = await _dialogs.PickExportFileToSaveAsync(fileName, format.Extension, format.Name, null, cancellationToken);

        if (string.IsNullOrEmpty(filePath))
        {
            return;
        }

        try
        {
            var manuscript = BuildManuscript();
            var options = BuildOptions();

            await Task.Run(
                () => WriteExport(format.Key, manuscript, filePath, options),
                cancellationToken);
            StatusMessage = Loc.F("Книга успешно экспортирована в файл \"{0}\"!", Path.GetFileName(filePath));
        }
        catch (Exception ex)
        {
            ErrorMessage = Loc.F("Ошибка при экспорте: {0}", ex.Message);
        }
    }

    [RelayCommand]
    private void SaveTemplate()
    {
        ErrorMessage = Loc.T("Сохранение шаблонов экспорта пока не реализовано.");
    }

    [RelayCommand]
    private void Preview()
    {
        UpdatePreview();
        StatusMessage = Loc.T("Предпросмотр обновлён.");
    }

    private ExportOptions BuildOptions()
    {
        var fontSize = double.TryParse(
            SelectedFontSize, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedSize)
            ? parsedSize
            : 12;
        var lineSpacing = double.TryParse(
            SelectedLineSpacing, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedSpacing)
            ? parsedSpacing
            : 1.5;

        return new ExportOptions(
            SelectedFont,
            fontSize,
            lineSpacing,
            IncludeTitlePage,
            TableOfContents,
            PageNumbering,
            IncludeSceneHeadings,
            IncludeCharacterCards);
    }

    private static void WriteExport(
        string format,
        ExportManuscript manuscript,
        string filePath,
        ExportOptions options)
    {
        switch (format)
        {
            case "DOCX":
                DocxExportWriter.Write(manuscript, filePath, options);
                break;
            case "PDF":
                PdfExportWriter.Write(manuscript, filePath, options);
                break;
            case "EPUB":
                EpubExportWriter.Write(manuscript, filePath, options);
                break;
            case "Markdown":
                TextExportWriter.WriteMarkdown(manuscript, filePath, options);
                break;
            default:
                TextExportWriter.WriteTxt(manuscript, filePath, options);
                break;
        }
    }

    private RichDocument? ReadSceneDocument(Guid sceneId)
    {
        var result = _projects.GetSceneDocument(sceneId);
        return result.TryGetValue(out var document) ? document.Document : null;
    }

    private void RebuildTranslatedOptions()
    {
        var formatKey = SelectedFormat?.Key ?? "TXT";
        var rangeKey = SelectedRange?.Key ?? "All";
        var styleIndex = Math.Max(0, DocumentStyles.ToList().IndexOf(SelectedDocumentStyle));
        _refreshingTranslations = true;
        try
        {
            Formats =
            [
                new("DOCX", "docx", "DOCX", "Microsoft Word, .docx", "W"),
                new("PDF", "pdf", "PDF", Loc.T("Печатный формат, .pdf"), "P"),
                new("EPUB", "epub", "EPUB", Loc.T("Электронная книга, .epub"), "E"),
                new("TXT", "txt", "TXT", Loc.T("Простой текст, .txt"), "T"),
                new("Markdown", "md", "Markdown", Loc.T("Текст с разметкой, .md"), "M"),
            ];
            Ranges =
            [
                new("All", Loc.T("Вся книга")),
                new("Book", Loc.T("Текущая книга")),
                new("Chapter", Loc.T("Текущая глава")),
                new("Scenes", Loc.T("Выбранные сцены")),
            ];
            DocumentStyles = [Loc.T("Классический (книжный)"), Loc.T("Стандартный"), Loc.T("Современный"), Loc.T("Академический")];
            OnPropertyChanged(nameof(Formats));
            OnPropertyChanged(nameof(Ranges));
            OnPropertyChanged(nameof(DocumentStyles));
            SelectedFormat = Formats.Single(option => option.Key == formatKey);
            SelectedRange = Ranges.Single(option => option.Key == rangeKey);
            SelectedDocumentStyle = DocumentStyles[styleIndex];
            OnPropertyChanged(nameof(SettingsHeader));
        }
        finally
        {
            _refreshingTranslations = false;
        }
    }

    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(LocalizationManager.Language)) return;
        RebuildTranslatedOptions();
        UpdatePreview();
    }

    public void Dispose()
    {
        _navigation.Navigated -= OnNavigated;
        LocalizationManager.Instance.PropertyChanged -= OnLanguageChanged;
    }
}
