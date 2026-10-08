namespace ScriSelfStudio.Presentation.Export;

using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ScriSelfStudio.Contracts.Books;
using ScriSelfStudio.Contracts.Dialogs;
using ScriSelfStudio.Contracts.Navigation;
using ScriSelfStudio.Contracts.Projects;
using ScriSelfStudio.Core.Model;
using ScriSelfStudio.Presentation;

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
    private string _selectedDocumentStyle = "Классический (книжный)";

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
    private bool _preserveFormatting = false;

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
            new("PDF", "pdf", "PDF", "Печатный формат, .pdf", "P"),
            new("EPUB", "epub", "EPUB", "Электронная книга, .epub", "E"),
            new("TXT", "txt", "TXT", "Простой текст, .txt", "T"),
            new("Markdown", "md", "Markdown", "Текст с разметкой, .md", "M"),
        ];

        Ranges =
        [
            new("All", "Вся книга"),
            new("Book", "Текущая книга"),
            new("Chapter", "Текущая глава"),
            new("Scenes", "Выбранные сцены"),
        ];

        DocumentStyles = ["Классический (книжный)", "Стандартный", "Современный", "Академический"];
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
            PreviewTitle = "Откройте проект";
            PreviewSubtitle = string.Empty;
            PreviewChapterHeader = string.Empty;
            PreviewPageCounter = string.Empty;
            return;
        }

        PreviewTitle = project.Name;
        var books = _architecture.GetBooks();
        var firstBook = books.FirstOrDefault();
        PreviewSubtitle = firstBook?.Title ?? "Книга 1";

        var firstChapter = firstBook?.Chapters.FirstOrDefault();
        PreviewChapterHeader = firstChapter != null ? $"Глава 1. {firstChapter.Title}" : "Глава 1. Начало";

        var scenesCount = books.Sum(b => b.Chapters.Sum(c => c.Scenes.Count));
        var firstScene = firstChapter?.Scenes.FirstOrDefault();
        if (firstScene != null)
        {
            PreviewTextExcerpt = ReadSceneText(firstScene.Id);
        }

        PreviewPageCounter = $"Сцен: {scenesCount}. Предпросмотр текста без пагинации.";
    }

    [RelayCommand]
    private async Task ExportAsync(CancellationToken cancellationToken = default)
    {
        StatusMessage = null;
        ErrorMessage = null;

        var format = SelectedFormat;
        if (!HasProject)
        {
            ErrorMessage = "Сначала откройте проект.";
            return;
        }
        if (format.Key is not ("TXT" or "Markdown"))
        {
            ErrorMessage = "Этот формат пока не реализован. Выберите TXT или Markdown.";
            return;
        }
        if (SelectedRange.Key != "All")
        {
            ErrorMessage = "Пока поддерживается экспорт всего проекта.";
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
            var content = BuildExportContent();
            await File.WriteAllTextAsync(filePath, content, Encoding.UTF8, cancellationToken);
            StatusMessage = $"Книга успешно экспортирована в файл \"{Path.GetFileName(filePath)}\"!";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Ошибка при экспорте: {ex.Message}";
        }
    }

    [RelayCommand]
    private void SaveTemplate()
    {
        ErrorMessage = "Сохранение шаблонов экспорта пока не реализовано.";
    }

    [RelayCommand]
    private void Preview()
    {
        UpdatePreview();
        StatusMessage = "Предпросмотр обновлён.";
    }

    private string BuildExportContent()
    {
        var sb = new StringBuilder();

        if (IncludeTitlePage)
        {
            sb.AppendLine("=========================================");
            sb.AppendLine($"         {ProjectTitle.ToUpperInvariant()}         ");
            sb.AppendLine($"         {PreviewSubtitle}         ");
            sb.AppendLine("=========================================");
            sb.AppendLine();
            sb.AppendLine();
        }

        var books = _architecture.GetBooks();

        if (TableOfContents)
        {
            sb.AppendLine("--- СОДЕРЖАНИЕ ---");
            int index = 1;
            foreach (var book in books)
            {
                foreach (var ch in book.Chapters)
                {
                    sb.AppendLine($"Глава {index++}. {ch.Title}");
                }
            }
            sb.AppendLine("------------------");
            sb.AppendLine();
        }

        int chIndex = 1;
        foreach (var book in books)
        {
            foreach (var chapter in book.Chapters)
            {
                sb.AppendLine($"{(SelectedFormat.Key == "Markdown" ? "# " : string.Empty)}Глава {chIndex++}. {chapter.Title}");
                sb.AppendLine();
                int scIndex = 1;
                foreach (var scene in chapter.Scenes)
                {
                    sb.AppendLine($"{(SelectedFormat.Key == "Markdown" ? "## " : string.Empty)}Сцена {scIndex++}: {scene.Title}");
                    var text = ReadSceneText(scene.Id);
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        sb.AppendLine(text);
                    }
                    sb.AppendLine();
                }
            }
        }

        return sb.ToString();
    }

    private string ReadSceneText(Guid sceneId) =>
        _projects.GetSceneDocument(sceneId).Require().Document.ToPlainText();

    public void Dispose() => _navigation.Navigated -= OnNavigated;
}
