namespace ScriSelfStudio.Presentation.Export;

using System.Collections.ObjectModel;
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
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    private string? _statusMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _errorMessage;

    [ObservableProperty]
    private string _previewTitle = "Пепел и Серебро";

    [ObservableProperty]
    private string _previewSubtitle = "Книга 1. Пробуждение";

    [ObservableProperty]
    private string _previewChapterHeader = "Глава 1. Начало";

    [ObservableProperty]
    private string _previewTextExcerpt = "Серые горы медленно уходили за горизонт, словно растворяясь в тумане. Дорога петляла между скал, и каждый поворот открывал новый, ещё более суровый вид...";

    [ObservableProperty]
    private string _previewPageCounter = "Страница 1 из 320";

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

        DocumentStyles =
        [
            Loc.T("Классический (книжный)"),
            Loc.T("Стандартный"),
            Loc.T("Современный"),
            Loc.T("Академический"),
        ];
        Fonts = ["Georgia", "Inter", "Times New Roman", "Segoe UI", "Consolas"];
        FontSizes = ["10", "11", "12", "14", "16"];
        LineSpacings = ["1.0", "1.15", "1.5", "2.0"];

        _selectedFormat = Formats.Single(format => format.Key == "TXT");
        _selectedRange = Ranges[0];

        _navigation.Navigated += OnNavigated;
        UpdatePreview();
    }

    public IReadOnlyList<ExportFormatOption> Formats { get; }

    public IReadOnlyList<ExportRangeOption> Ranges { get; }

    public IReadOnlyList<string> DocumentStyles { get; }

    public IReadOnlyList<string> Fonts { get; }

    public IReadOnlyList<string> FontSizes { get; }

    public IReadOnlyList<string> LineSpacings { get; }

    public bool HasStatus => !string.IsNullOrEmpty(StatusMessage);

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public bool HasProject => _projects.Current is not null;

    public string ProjectTitle => _projects.Current?.Name ?? "Пепел и Серебро";

    public string SettingsHeader => Loc.F("Настройки экспорта ({0})", SelectedFormat.Name);

    partial void OnSelectedFormatChanged(ExportFormatOption value) =>
        OnPropertyChanged(nameof(SettingsHeader));

    /// <summary>
    /// Стиль документа задаёт согласованный набор шрифта, размера и интервала.
    /// Дальше автор может поправить любой из этих параметров отдельно.
    /// </summary>
    partial void OnSelectedDocumentStyleChanged(string value)
    {
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
        PreviewTextExcerpt = string.Empty;
        var project = _projects.Current;
        if (project is null)
        {
            PreviewTitle = Loc.T("Откройте проект");
            PreviewSubtitle = string.Empty;
            PreviewChapterHeader = string.Empty;
            PreviewPageCounter = string.Empty;
            return;
        }

        PreviewTitle = project.Name;
        var books = _architecture.GetBooks();
        var firstBook = books.FirstOrDefault();
        PreviewSubtitle = firstBook?.Title ?? Loc.T("Книга 1");

        var firstChapter = firstBook?.Chapters.FirstOrDefault();
        PreviewChapterHeader = firstChapter != null
            ? Loc.F("Глава 1. {0}", firstChapter.Title)
            : Loc.T("Глава 1. Начало");

        var scenesCount = books.Sum(b => b.Chapters.Sum(c => c.Scenes.Count));
        var firstScene = firstChapter?.Scenes.FirstOrDefault();
        if (firstScene != null)
        {
            PreviewTextExcerpt = ReadSceneText(firstScene.Id);
        }

        PreviewPageCounter = Loc.F("Сцен: {0}. Предпросмотр текста без пагинации.", scenesCount);
    }

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
            var manuscript = ManuscriptExportBuilder.Build(
                ProjectTitle,
                _architecture.GetBooks(),
                ReadSceneDocument,
                PreserveFormatting);
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
            PageNumbering);
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

    private string ReadSceneText(Guid sceneId) =>
        _projects.GetSceneDocument(sceneId).Require().Document.ToPlainText();

    public void Dispose() => _navigation.Navigated -= OnNavigated;
}
