namespace ScriSelfStudio.EditorUI;

using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using ScriSelfStudio.Contracts.Editor;
using ScriSelfStudio.Contracts.Projects;
using ScriSelfStudio.Contracts.Settings;
using ScriSelfStudio.Presentation;
using ScriSelfStudio.Presentation.Localization;

public sealed partial class TextToolsViewModel : ViewModelBase
{
    private readonly IAppSettingsService _settings;
    private readonly IProjectService _projects;
    private readonly HashSet<string> _ignored = new(StringComparer.OrdinalIgnoreCase);
    private bool _refreshingPreferences;

    [ObservableProperty]
    private bool _isEnabled;

    private string _status = "Русский и английский · проверка на устройстве";
    private object?[] _statusArguments = [];

    public string Status
    {
        get => _statusArguments.Length == 0 ? Loc.T(_status) : Loc.F(_status, _statusArguments);
        set => SetStatus(value);
    }

    public void SetStatus(string message, params object?[] arguments)
    {
        _status = message;
        _statusArguments = arguments;
        OnPropertyChanged(nameof(Status));
    }

    public void RefreshLocalization() => OnPropertyChanged(nameof(Status));

    public TextToolsViewModel(ITextToolsService service, IAppSettingsService settings, IProjectService projects)
    {
        Service = service;
        _settings = settings;
        _projects = projects;
        _isEnabled = settings.Current.SpellCheckEnabled;
    }

    public ITextToolsService Service { get; }

    public bool AutoCapitalizeSentences => _settings.Current.AutoCapitalizeSentences;

    public event EventHandler? DictionaryChanged;

    public void RefreshPreferences()
    {
        _refreshingPreferences = true;
        try { IsEnabled = _settings.Current.SpellCheckEnabled; }
        finally { _refreshingPreferences = false; }
    }

    partial void OnIsEnabledChanged(bool value)
    {
        if (!_refreshingPreferences) _ = SaveEnabledAsync(value);
        DictionaryChanged?.Invoke(this, EventArgs.Empty);
    }

    public HashSet<string> KnownWords()
    {
        var result = new HashSet<string>(_settings.Current.PersonalDictionary ?? [], StringComparer.OrdinalIgnoreCase);
        result.UnionWith(_ignored);
        foreach (var character in _projects.Current?.Characters ?? [])
        {
            foreach (Match match in NameWords().Matches(character.Name)) result.Add(match.Value);
        }
        return result;
    }

    public void Ignore(string word)
    {
        _ignored.Add(word);
        DictionaryChanged?.Invoke(this, EventArgs.Empty);
    }

    public bool IsPersonalWord(string word) => (_settings.Current.PersonalDictionary ?? []).Contains(word, StringComparer.OrdinalIgnoreCase);

    public async Task SetPersonalWordAsync(string word, bool include)
    {
        var saved = await _settings.UpdateAsync(p =>
        {
            p.PersonalDictionary ??= [];
            p.PersonalDictionary.RemoveAll(w => w.Equals(word, StringComparison.OrdinalIgnoreCase));
            if (include) p.PersonalDictionary.Add(word);
        });
        if (saved.IsSuccess) Status = include ? "Слово добавлено в личный словарь" : "Слово удалено из личного словаря";
        else SetStatus("Не удалось сохранить личный словарь: {0}", saved.Error!.UserMessage);
        DictionaryChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task SaveEnabledAsync(bool value)
    {
        var result = await _settings.UpdateAsync(p => p.SpellCheckEnabled = value);
        if (result.IsSuccess) Status = value ? "Русский и английский · проверка на устройстве" : "Проверка орфографии выключена";
        else SetStatus("Не удалось сохранить настройку: {0}", result.Error!.UserMessage);
    }

    [GeneratedRegex("[A-Za-zА-Яа-яЁё]+", RegexOptions.CultureInvariant)]
    private static partial Regex NameWords();
}
