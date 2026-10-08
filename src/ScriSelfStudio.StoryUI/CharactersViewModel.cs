namespace ScriSelfStudio.StoryUI;

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ScriSelfStudio.Contracts.Books;
using ScriSelfStudio.Contracts.Dialogs;
using ScriSelfStudio.Contracts.Errors;
using ScriSelfStudio.Contracts.Navigation;
using ScriSelfStudio.Contracts.Projects;
using ScriSelfStudio.Core.Model;
using ScriSelfStudio.Presentation;

/// <summary>Строка списка персонажей: имя и краткая сводка карточки.</summary>
public sealed record CharacterListItem(
    Guid Id,
    string Name,
    string RoleLabel,
    string StatusLabel,
    int ArcStageCount)
{
    public string Summary => $"{RoleLabel} · {StatusLabel} · этапов арки: {ArcStageCount}";
}

/// <summary>
/// Экран персонажей: список и подробная карточка из 14 разделов (ТЗ п. 27, 35; ADR 010).
/// Карточка отправляется в сервис целиком, правила хранения живут в нём.
/// </summary>
public sealed partial class CharactersViewModel : ViewModelBase, IDisposable
{
    private readonly IBookArchitectureService _architecture;
    private readonly IProjectService _projects;
    private readonly INavigationService _navigation;
    private readonly IFileDialogService _dialogs;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private CharacterListItem? _selected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNew))]
    [NotifyPropertyChangedFor(nameof(CardTitle))]
    private CharacterDraft _draft = CharacterDraft.New();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _errorMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    private string? _statusMessage;

    public CharactersViewModel(
        IBookArchitectureService architecture,
        IProjectService projects,
        INavigationService navigation,
        IFileDialogService dialogs)
    {
        _architecture = architecture;
        _projects = projects;
        _navigation = navigation;
        _dialogs = dialogs;
        Sections = BuildSections();
        _navigation.Navigated += OnNavigated;
        Refresh();
    }

    public ObservableCollection<CharacterListItem> Characters { get; } = [];

    /// <summary>Разделы 2–13 карточки: одинаковые поля-абзацы показывает один шаблон.</summary>
    public IReadOnlyList<CardSection> Sections { get; }

    /// <summary>Где персонаж участвует в сюжете (раздел 10 карточки) — читается из линий.</summary>
    public ObservableCollection<string> Participation { get; } = [];

    private readonly List<CardField> _fields = [];

    /// <summary>Все персонажи проекта — кандидаты для раздела «Отношения».</summary>
    public IReadOnlyList<CharacterChoice> CharacterChoices => [.. Characters.Select(item => new CharacterChoice(item.Id, item.Name))];

    public IReadOnlyList<Choice<CharacterRole>> Roles => StoryLabels.Roles;

    public IReadOnlyList<Choice<CharacterImportance>> Importances => StoryLabels.Importances;

    public IReadOnlyList<Choice<CharacterStatus>> Statuses => StoryLabels.Statuses;

    public IReadOnlyList<Choice<CharacterArcType>> ArcTypes => StoryLabels.ArcTypes;

    public IReadOnlyList<Choice<CharacterDraftStatus>> DraftStatuses => StoryLabels.DraftStatuses;

    public IReadOnlyList<Choice<CharacterRelationType>> RelationTypes => StoryLabels.RelationTypes;

    public IReadOnlyList<Choice<CharacterArcStageKind>> ArcStageKinds => StoryLabels.ArcStageKinds;

    public bool HasProject => _projects.Current is not null;

    public string ProjectTitle => _projects.Current?.Name ?? "Проект не открыт";

    public bool HasSelection => Selected is not null;

    public bool IsNew => Draft.IsNew;

    public string CardTitle => Draft.IsNew ? "Новый персонаж" : Draft.Name;

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public bool HasStatus => !string.IsNullOrEmpty(StatusMessage);

    public bool HasCharacters => Characters.Count > 0;

    public int SelectionIndex => Selected is null ? -1 : Characters.IndexOf(Selected);

    /// <summary>Перечитать список персонажей из сервиса структуры, сохранив выбор.</summary>
    public void Refresh()
    {
        var selectedId = Selected?.Id;
        Characters.Clear();
        foreach (var character in _architecture.GetCharacters())
        {
            Characters.Add(new CharacterListItem(
                character.Id,
                character.Name,
                StoryLabels.RoleLabel(character.Role),
                StoryLabels.StatusLabel(character.Status),
                character.ArcStages.Count));
        }

        if (selectedId is { } id && Characters.FirstOrDefault(item => item.Id == id) is { } restored)
        {
            Selected = restored;
            LoadDraft(restored.Id);
        }
        else
        {
            Selected = null;
            Draft = CharacterDraft.New();
        }

        OnPropertyChanged(nameof(HasProject));
        OnPropertyChanged(nameof(ProjectTitle));
        OnPropertyChanged(nameof(HasCharacters));
        OnPropertyChanged(nameof(CharacterChoices));
        NotifyCommands();
    }

    [RelayCommand]
    private void NewCharacter()
    {
        ClearMessages();
        Selected = null;
        Draft = CharacterDraft.New();
        NotifyCommands();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task DeleteCharacterAsync(CancellationToken cancellationToken)
    {
        if (Selected is not { } item)
        {
            return;
        }

        var confirmed = await _dialogs.ConfirmAsync(
            "Удаление персонажа",
            $"Удалить «{item.Name}»? Участие в шагах сюжетных линий тоже будет убрано.",
            "Удалить",
            "Отмена",
            cancellationToken);
        if (!confirmed)
        {
            return;
        }

        ClearMessages();
        var result = await _architecture.DeleteCharacterAsync(item.Id, cancellationToken);
        if (!result.IsSuccess)
        {
            await ReportAsync(result.Error!);
            return;
        }

        Selected = null;
        Refresh();
        StatusMessage = $"Персонаж «{item.Name}» удалён.";
    }

    [RelayCommand]
    private async Task SaveCharacterAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(Draft.Name))
        {
            ErrorMessage = "Укажите имя персонажа.";
            return;
        }

        ClearMessages();
        var dto = Draft.ToDto();
        var result = await _architecture.SaveCharacterAsync(dto, cancellationToken);
        if (!result.IsSuccess)
        {
            await ReportAsync(result.Error!);
            return;
        }

        var saved = result.Require().Characters.FirstOrDefault(character => character.Name == dto.Name);
        Refresh();
        if (saved is not null)
        {
            Selected = Characters.FirstOrDefault(item => item.Id == saved.Id);
        }

        StatusMessage = dto.Id == Guid.Empty
            ? $"Персонаж «{dto.Name}» создан."
            : $"Карточка «{dto.Name}» сохранена.";
    }

    [RelayCommand]
    private void AddArcStage() =>
        Draft.ArcStages.Add(new ArcStageDraft(
            Guid.Empty,
            "Новый этап",
            CharacterArcStageKind.Custom,
            null));

    [RelayCommand]
    private void RemoveArcStage(ArcStageDraft? step)
    {
        if (step is not null)
        {
            Draft.ArcStages.Remove(step);
        }
    }

    [RelayCommand]
    private void AddRelation() =>
        Draft.Relations.Add(new RelationDraft(
            Draft.RelationCandidates(CharacterChoices),
            Guid.Empty,
            CharacterRelationType.Other,
            null,
            null));

    [RelayCommand]
    private void RemoveRelation(RelationDraft? relation)
    {
        if (relation is not null)
        {
            Draft.Relations.Remove(relation);
        }
    }

    [RelayCommand]
    private void GoBack() => _navigation.GoBack();

    public void Dispose() => _navigation.Navigated -= OnNavigated;

    partial void OnSelectedChanged(CharacterListItem? value)
    {
        if (value is not null && value.Id != Draft.Id)
        {
            LoadDraft(value.Id);
        }

        NotifyCommands();
    }

    /// <summary>Карточка заменена целиком: поля перечитывают значения и участие в сюжете.</summary>
    partial void OnDraftChanged(CharacterDraft value)
    {
        foreach (var field in _fields)
        {
            field.Refresh();
        }

        RefreshParticipation();
    }

    private void RefreshParticipation()
    {
        Participation.Clear();
        if (Draft.IsNew)
        {
            return;
        }

        foreach (var line in _architecture.GetPlotLines())
        {
            foreach (var step in line.Steps)
            {
                if (step.Participants.Any(participant => participant.CharacterId == Draft.Id))
                {
                    var arcStageId = step.Participants
                        .Where(participant => participant.CharacterId == Draft.Id)
                        .Select(participant => participant.CharacterArcStageId)
                        .FirstOrDefault();
                    var stepTitle = arcStageId is null
                        ? "этап арки не выбран"
                        : Draft.ArcStages.FirstOrDefault(item => item.Id == arcStageId)?.Title ?? "этап арки не выбран";
                    Participation.Add($"{line.Name} · {step.Title} — {stepTitle}");
                }
            }
        }
    }

    private CardField Field(string label, Func<string?> get, Action<string?> set, bool multiline = false)
    {
        var field = new CardField(label, get, set, multiline);
        _fields.Add(field);
        return field;
    }

    /// <summary>Разделы 2–13: подписи по ТЗ, значения — свободный текст карточки.</summary>
    private IReadOnlyList<CardSection> BuildSections() =>
    [
        new("2. Внешность",
        [
            Field("Внешность", () => Draft.Appearance, v => Draft.Appearance = v, multiline: true),
            Field("Голос", () => Draft.Voice, v => Draft.Voice = v, multiline: true),
            Field("Мимика и жесты", () => Draft.Mannerisms, v => Draft.Mannerisms = v, multiline: true),
            Field("Внешность в состояниях", () => Draft.AppearanceStates, v => Draft.AppearanceStates = v, multiline: true),
            Field("Референсы", () => Draft.References, v => Draft.References = v, multiline: true),
        ]),
        new("3. Личность и психология",
        [
            Field("Архетип", () => Draft.Archetype, v => Draft.Archetype = v),
            Field("Темперамент", () => Draft.Temperament, v => Draft.Temperament = v),
            Field("Ценности", () => Draft.Values, v => Draft.Values = v, multiline: true),
            Field("Убеждения", () => Draft.Beliefs, v => Draft.Beliefs = v, multiline: true),
            Field("Ложь (в чём ошибается)", () => Draft.FalseBelief, v => Draft.FalseBelief = v, multiline: true),
            Field("Потребность", () => Draft.InternalNeed, v => Draft.InternalNeed = v, multiline: true),
            Field("Желание (Want)", () => Draft.Want, v => Draft.Want = v, multiline: true),
            Field("Страхи", () => Draft.Fears, v => Draft.Fears = v, multiline: true),
            Field("Слабости", () => Draft.Weaknesses, v => Draft.Weaknesses = v, multiline: true),
            Field("Сильные стороны", () => Draft.Strengths, v => Draft.Strengths = v, multiline: true),
            Field("Привычки и хобби", () => Draft.Habits, v => Draft.Habits = v, multiline: true),
            Field("Юмор", () => Draft.Humour, v => Draft.Humour = v, multiline: true),
            Field("Защитные механизмы", () => Draft.DefenceMechanisms, v => Draft.DefenceMechanisms = v, multiline: true),
            Field("Копинг-стратегии", () => Draft.CopingStrategies, v => Draft.CopingStrategies = v, multiline: true),
        ]),
        new("4. Мотивация и цели",
        [
            Field("Главная цель", () => Draft.ExternalGoal, v => Draft.ExternalGoal = v, multiline: true),
            Field("Тайная цель", () => Draft.SecretGoal, v => Draft.SecretGoal = v, multiline: true),
            Field("Мотивация", () => Draft.Motivation, v => Draft.Motivation = v, multiline: true),
            Field("Готов сделать ради цели", () => Draft.ReadyToDo, v => Draft.ReadyToDo = v, multiline: true),
            Field("Никогда не сделает", () => Draft.NeverDo, v => Draft.NeverDo = v, multiline: true),
            Field("Что для него победа", () => Draft.Victory, v => Draft.Victory = v, multiline: true),
            Field("Что для него провал", () => Draft.Failure, v => Draft.Failure = v, multiline: true),
        ]),
        new("5. Навыки и способности",
        [
            Field("Образование", () => Draft.Education, v => Draft.Education = v, multiline: true),
            Field("Профессиональные навыки", () => Draft.Skills, v => Draft.Skills = v, multiline: true),
            Field("Боевые умения", () => Draft.Combat, v => Draft.Combat = v, multiline: true),
            Field("Магия и суперсилы", () => Draft.Powers, v => Draft.Powers = v, multiline: true),
            Field("Таланты", () => Draft.Talents, v => Draft.Talents = v, multiline: true),
            Field("Ограничения и слабости", () => Draft.Limitations, v => Draft.Limitations = v, multiline: true),
            Field("Инвентарь", () => Draft.Inventory, v => Draft.Inventory = v, multiline: true),
            Field("Транспорт, питомцы, слуги", () => Draft.Companions, v => Draft.Companions = v, multiline: true),
        ]),
        new("7. Речь и манера общения",
        [
            Field("Лексика", () => Draft.Speech, v => Draft.Speech = v, multiline: true),
            Field("Акцент, диалект", () => Draft.Accent, v => Draft.Accent = v, multiline: true),
            Field("Любимые фразы, словечки", () => Draft.SpeechHabits, v => Draft.SpeechHabits = v, multiline: true),
            Field("Избегаемые темы", () => Draft.SpeechAvoided, v => Draft.SpeechAvoided = v, multiline: true),
            Field("Речь в стрессе", () => Draft.SpeechUnderStress, v => Draft.SpeechUnderStress = v, multiline: true),
        ]),
        new("8. Биография (до начала истории)",
        [
            Field("Место рождения", () => Draft.Birthplace, v => Draft.Birthplace = v),
            Field("Детство: ключевые события", () => Draft.Childhood, v => Draft.Childhood = v, multiline: true),
            Field("Травмы", () => Draft.Trauma, v => Draft.Trauma = v, multiline: true),
            Field("Образование и работа", () => Draft.Career, v => Draft.Career = v, multiline: true),
            Field("Ключевые повороты судьбы", () => Draft.TurningPoints, v => Draft.TurningPoints = v, multiline: true),
            Field("Как оказался в точке старта", () => Draft.StoryStart, v => Draft.StoryStart = v, multiline: true),
            Field("Хронология событий", () => Draft.Chronology, v => Draft.Chronology = v, multiline: true),
        ]),
        new("11. Секреты и тайны",
        [
            Field("Скрывает от других", () => Draft.Secrets, v => Draft.Secrets = v, multiline: true),
            Field("Скрывает от себя", () => Draft.SecretsFromSelf, v => Draft.SecretsFromSelf = v, multiline: true),
            Field("Кто знает правду", () => Draft.SecretKeepers, v => Draft.SecretKeepers = v, multiline: true),
            Field("Когда и как раскроется", () => Draft.SecretReveal, v => Draft.SecretReveal = v, multiline: true),
            Field("Последствия раскрытия", () => Draft.SecretConsequences, v => Draft.SecretConsequences = v, multiline: true),
        ]),
        new("12. Развитие в истории",
        [
            Field("Меняется внешне", () => Draft.PhysicalChange, v => Draft.PhysicalChange = v, multiline: true),
            Field("Меняется психологически", () => Draft.PsychologicalChange, v => Draft.PsychologicalChange = v, multiline: true),
            Field("Меняются отношения", () => Draft.RelationshipChange, v => Draft.RelationshipChange = v, multiline: true),
            Field("Ключевые решения", () => Draft.KeyDecisions, v => Draft.KeyDecisions = v, multiline: true),
            Field("Что теряет", () => Draft.Loses, v => Draft.Loses = v, multiline: true),
            Field("Что приобретает", () => Draft.Gains, v => Draft.Gains = v, multiline: true),
        ]),
        new("13. Заметки и цитаты",
        [
            Field("Идеи для сцен", () => Draft.SceneIdeas, v => Draft.SceneIdeas = v, multiline: true),
            Field("Вдохновляющие цитаты", () => Draft.Quotes, v => Draft.Quotes = v, multiline: true),
            Field("Вопросы для проработки", () => Draft.OpenQuestions, v => Draft.OpenQuestions = v, multiline: true),
            Field("Свободные заметки", () => Draft.Notes, v => Draft.Notes = v, multiline: true),
        ]),
    ];

    private void OnNavigated(object? sender, AppScreen screen)
    {
        if (screen == AppScreen.Characters)
        {
            Refresh();
        }
    }

    private void LoadDraft(Guid characterId)
    {
        var card = _architecture.GetCharacters().FirstOrDefault(character => character.Id == characterId);
        Draft = card is null ? CharacterDraft.New() : CharacterDraft.From(card, CharacterChoices);
    }

    private async Task ReportAsync(AppError error)
    {
        ErrorMessage = error.SuggestedAction is null
            ? error.UserMessage
            : $"{error.UserMessage} {error.SuggestedAction}";
        await _dialogs.ShowErrorAsync("Ошибка персонажа", ErrorMessage);
    }

    private void ClearMessages()
    {
        ErrorMessage = null;
        StatusMessage = null;
    }

    private void NotifyCommands()
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(IsNew));
        OnPropertyChanged(nameof(CardTitle));
        DeleteCharacterCommand.NotifyCanExecuteChanged();
    }
}