namespace ScriSelfStudio.Presentation.Help;

using System.Reflection;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

public sealed record HelpBlock(string Heading, string Text);

public sealed record HelpTopic(string Id, string Title, string Category, string Summary, HelpBlock[] Blocks)
{
    public bool IsLicenses => Id == "licenses";

    public bool Matches(string term)
    {
        var text = $"{Title} {Category} {Summary} {string.Join(' ', Blocks.Select(b => b.Heading + " " + b.Text))}"
            .Replace('ё', 'е').Replace('Ё', 'Е');
        var query = term.Replace('ё', 'е').Replace('Ё', 'Е');
        for (var start = 0; start < text.Length;)
        {
            var index = text.IndexOf(query, start, StringComparison.OrdinalIgnoreCase);
            if (index < 0) return false;
            // Prefixes still find inflected words, but «цена» must not match «сцена».
            if (index == 0 || !char.IsLetterOrDigit(text[index - 1])) return true;
            start = index + Math.Max(1, query.Length);
        }
        return false;
    }
}

public sealed record LicenseDocument(string Title, string Text);

/// <summary>Вся справка и тексты лицензий встроены в сборку, сеть и проект не требуются.</summary>
public sealed partial class HelpViewModel : ViewModelBase
{
    private const string ResourcePrefix = "ScriSelfStudio.Help.";
    private static readonly Assembly Resources = typeof(HelpViewModel).Assembly;
    private readonly IReadOnlyList<HelpTopic> _topics;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<HelpTopic> _filteredTopics = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTopic))]
    [NotifyPropertyChangedFor(nameof(ShowLicenses))]
    [NotifyPropertyChangedFor(nameof(SelectedTitle))]
    [NotifyPropertyChangedFor(nameof(SelectedSummary))]
    [NotifyPropertyChangedFor(nameof(SelectedBlocks))]
    private HelpTopic? _selectedTopic;

    [ObservableProperty]
    private LicenseDocument? _selectedLicense;

    public HelpViewModel()
    {
        _topics = JsonSerializer.Deserialize<HelpTopic[]>(ReadResource("guide.json"))
            ?? throw new InvalidOperationException("Встроенная справка пуста.");
        Licenses = Resources.GetManifestResourceNames()
            .Where(name => name.StartsWith(ResourcePrefix + "Licenses.", StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)
            .Select(name => new LicenseDocument(
                name[(ResourcePrefix + "Licenses.").Length..^4].Replace('_', ' '), ReadFullResource(name)))
            .ToArray();
        SelectedLicense = Licenses.FirstOrDefault();
        FilteredTopics = _topics;
        SelectTopic("start");
    }

    public string VersionText => $"Версия {Resources.GetName().Version?.ToString(3)}";

    public IReadOnlyList<LicenseDocument> Licenses { get; }

    public event EventHandler? TrainingRequested;

    [RelayCommand]
    private void OpenTraining() => TrainingRequested?.Invoke(this, EventArgs.Empty);

    public bool HasTopic => SelectedTopic is not null;

    public bool ShowLicenses => SelectedTopic?.IsLicenses == true;

    public string SelectedTitle => SelectedTopic?.Title ?? string.Empty;

    public string SelectedSummary => SelectedTopic?.Summary ?? string.Empty;

    public IReadOnlyList<HelpBlock> SelectedBlocks => SelectedTopic?.Blocks ?? [];

    partial void OnSearchQueryChanged(string value)
    {
        var terms = value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var previous = SelectedTopic;
        FilteredTopics = _topics.Where(topic => terms.All(topic.Matches)).ToArray();
        SelectedTopic = previous is not null && FilteredTopics.Contains(previous) ? previous : FilteredTopics.FirstOrDefault();
    }

    [RelayCommand]
    public void SelectTopic(string? id)
    {
        SearchQuery = string.Empty;
        SelectedTopic = _topics.FirstOrDefault(topic => topic.Id == id) ?? _topics[0];
    }

    [RelayCommand]
    private void ClearSearch() => SearchQuery = string.Empty;

    private static string ReadResource(string name) => ReadFullResource(ResourcePrefix + name);

    private static string ReadFullResource(string name)
    {
        using var stream = Resources.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Не найден ресурс справки: {name}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
