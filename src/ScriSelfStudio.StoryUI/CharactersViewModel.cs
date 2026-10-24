namespace ScriSelfStudio.StoryUI;

using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ScriSelfStudio.Contracts.Books;
using ScriSelfStudio.Contracts.Dialogs;
using ScriSelfStudio.Contracts.Dtos;
using ScriSelfStudio.Contracts.Errors;
using ScriSelfStudio.Contracts.Navigation;
using ScriSelfStudio.Contracts.Projects;
using ScriSelfStudio.Core.Model;
using ScriSelfStudio.Presentation;
using ScriSelfStudio.Presentation.Localization;

/// <summary>Строка списка персонажей: имя и краткая сводка карточки.</summary>
public sealed record CharacterListItem(
    Guid Id,
    string Name,
    string RoleLabel,
    string StatusLabel,
    int ArcStageCount)
{
    public string Summary => $"{RoleLabel} · {StatusLabel} · {Loc.F("этапов арки: {0}", ArcStageCount)}";
}


/// <summary>Связь персонажа в правой панели карточки: контакт и подпись отношения.</summary>
public sealed record RelatedCharacterEntry(string Initial, string Name, string RelationLabel, IBrush Brush);

/// <summary>Линия, к шагам которой персонаж причастен (правая панель карточки).</summary>
public sealed record RelatedLineEntry(string Name, IBrush Brush, IBrush StatusBrush, string StatusLabel);
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
        _isCharactersScreen = _navigation.Current == AppScreen.Characters;
        Refresh();
    }

    public ObservableCollection<CharacterListItem> Characters { get; } = [];

    /// <summary>Разделы 2–13 карточки: одинаковые поля-абзацы показывает один шаблон.</summary>
    public IReadOnlyList<CardSection> Sections { get; }

    /// <summary>Где персонаж участвует в сюжете (раздел 10 карточки) — читается из линий.</summary>
    public ObservableCollection<string> Participation { get; } = [];

    /// <summary>Правая панель: связи карточки (раздел 6), читаются из черновика.</summary>
    public ObservableCollection<RelatedCharacterEntry> RelatedCharacters { get; } = [];

    /// <summary>Правая панель: линии, к шагам которых персонаж причастен.</summary>
    public ObservableCollection<RelatedLineEntry> RelatedLines { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SceneCountLabel))]
    [NotifyPropertyChangedFor(nameof(HasScenePresence))]
    private int _sceneCount;

    public string SceneCountLabel => SceneCount == 0
        ? Loc.T("Нет сцен")
        : $"{SceneCount} {Loc.T(SceneWord(SceneCount))}";

    public bool HasRelatedCharacters => RelatedCharacters.Count > 0;

    public bool HasRelatedLines => RelatedLines.Count > 0;

    public bool HasScenePresence => SceneCount > 0;
    private readonly List<CardField> _fields = [];

    private static readonly string[] RelatedAvatarColors =
        ["#8C6D58", "#4A5568", "#718096", "#5A6B7A", "#5B7BB2", "#4F6D46"];

    private CharacterDraft? _watchedDraft;
    private bool _isCharactersScreen;
    private bool _suppressSelectionFlush;
    private string? _savedSignature;
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

    public string ProjectTitle => _projects.Current?.Name ?? Loc.T("Проект не открыт");

    public bool HasSelection => Selected is not null;

    public bool IsNew => Draft.IsNew;

    public string CardTitle => Draft.IsNew ? Loc.T("Новый персонаж") : Draft.Name;

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public bool HasStatus => !string.IsNullOrEmpty(StatusMessage);

    public bool HasCharacters => Characters.Count > 0;

    public int SelectionIndex => Selected is null ? -1 : Characters.IndexOf(Selected);

    /// <summary>Перечитать список персонажей из сервиса структуры, сохранив выбор.</summary>
    public void Refresh() => RefreshTo(null);

    /// <summary>Перечитать список и открыть карточку <paramref name="preferredId"/>, если она есть.</summary>
    private void RefreshTo(Guid? preferredId)
    {
        var selectedId = preferredId ?? Selected?.Id;
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
            // Перечитывание списка после сохранения не является выбором автора:
            // без флага только что сохранённая карточка записывалась бы повторно.
            _suppressSelectionFlush = true;
            try
            {
                Selected = restored;
            }
            finally
            {
                _suppressSelectionFlush = false;
            }

            LoadDraft(restored.Id);
        }
        else
        {
            Selected = null;
            Draft = CharacterDraft.New();
            _savedSignature = Signature(Draft.ToDto());
        }

        OnPropertyChanged(nameof(HasProject));
        OnPropertyChanged(nameof(ProjectTitle));
        OnPropertyChanged(nameof(HasCharacters));
        OnPropertyChanged(nameof(CharacterChoices));
        NotifyCommands();
    }

    [RelayCommand]
    private async Task NewCharacterAsync(CancellationToken cancellationToken)
    {
        await FlushDraftAsync(cancellationToken);
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
            Loc.T("Удаление персонажа"),
            Loc.F("Удалить «{0}»? Участие в шагах сюжетных линий тоже будет убрано.", item.Name),
            Loc.T("Удалить"),
            Loc.T("Отмена"),
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
        StatusMessage = Loc.F("Персонаж «{0}» удалён.", item.Name);
    }

    [RelayCommand]
    private Task SaveCharacterAsync(CancellationToken cancellationToken) =>
        PersistDraftAsync(selectSaved: true, showErrors: true, announce: true, cancellationToken);

    /// <summary>
    /// Автосохранение карточки: вызывается при потере фокуса полем, смене
    /// персонажа и уходе с экрана. Ошибки показываются в самой карточке,
    /// чтобы фоновое сохранение не открывало модальное окно.
    /// </summary>
    public Task CommitDraftAsync(CancellationToken cancellationToken = default) =>
        PersistDraftAsync(selectSaved: true, showErrors: false, announce: false, cancellationToken);

    /// <summary>Есть ли правки, которые ещё не записаны в проект.</summary>
    public bool HasUnsavedDraft => !string.IsNullOrWhiteSpace(Draft.Name)
        && (Draft.IsNew || Signature(Draft.ToDto()) != _savedSignature);

    /// <summary>
    /// Фоновая фиксация черновика без перечитывания выбранной карточки:
    /// применяется перед переключением на другого персонажа и при выходе.
    /// </summary>
    public async Task FlushDraftAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(Draft.Name))
        {
            return;
        }

        var dto = Draft.ToDto();
        if (!Draft.IsNew && Signature(dto) == _savedSignature)
        {
            return;
        }

        var result = await _architecture.SaveCharacterAsync(dto, cancellationToken);
        if (!result.IsSuccess)
        {
            ErrorMessage = result.Error!.SuggestedAction is null
                ? Loc.T(result.Error.UserMessage)
                : $"{Loc.T(result.Error.UserMessage)} {Loc.T(result.Error.SuggestedAction)}";
            return;
        }

        var saved = result.Require().Characters.FirstOrDefault(character =>
            dto.Id == Guid.Empty ? character.Name == dto.Name : character.Id == dto.Id);
        if (saved is null)
        {
            return;
        }

        UpdateListItem(saved);
        if (dto.Id != Guid.Empty && Draft.Id == saved.Id)
        {
            _savedSignature = Signature(saved);
        }
    }

    private async Task<bool> PersistDraftAsync(
        bool selectSaved,
        bool showErrors,
        bool announce,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(Draft.Name))
        {
            if (showErrors)
            {
                ErrorMessage = Loc.T("Укажите имя персонажа.");
            }

            return false;
        }

        var draftId = Draft.Id;
        var selectedBefore = Selected?.Id;
        ClearMessages();
        var dto = Draft.ToDto();
        var signature = Signature(dto);
        if (!announce && !Draft.IsNew && signature == _savedSignature)
        {
            return false;
        }

        var result = await _architecture.SaveCharacterAsync(dto, cancellationToken);
        if (!result.IsSuccess)
        {
            ErrorMessage = result.Error!.SuggestedAction is null
                ? Loc.T(result.Error.UserMessage)
                : $"{Loc.T(result.Error.UserMessage)} {Loc.T(result.Error.SuggestedAction)}";
            if (showErrors)
            {
                await _dialogs.ShowErrorAsync(Loc.T("Ошибка персонажа"), ErrorMessage);
            }

            return false;
        }

        var saved = result.Require().Characters.FirstOrDefault(character =>
            draftId == Guid.Empty ? character.Name == dto.Name : character.Id == draftId);

        // Пока шло сохранение, автор мог выбрать другую карточку. В этом случае
        // не перетягиваем выделение обратно на только что сохранённого персонажа.
        var selectedAfterAwait = Selected?.Id;
        var preserveUserSelection = selectedAfterAwait is { } current
            && current != selectedBefore
            && current != saved?.Id;
        var preferred = preserveUserSelection
            ? selectedAfterAwait
            : selectSaved
                ? saved?.Id
                : selectedAfterAwait ?? selectedBefore;
        RefreshTo(preferred);

        if (announce)
        {
            StatusMessage = dto.Id == Guid.Empty
                ? Loc.F("Персонаж «{0}» создан.", dto.Name)
                : Loc.F("Карточка «{0}» сохранена.", dto.Name);
        }

        return true;
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
            // Черновик прежней карточки фиксируем до его замены: иначе клик
            // по другому персонажу затирал несохранённое описание.
            if (!_suppressSelectionFlush)
            {
                _ = FlushDraftAsync();
            }

            LoadDraft(value.Id);
        }

        NotifyCommands();
    }

    /// <summary>
    /// Карточка заменена целиком: поля перечитывают значения, участие в сюжете и правую панель.
    /// Связи подписаны на изменения состава, чтобы панель не отставала от правок.
    /// </summary>
    partial void OnDraftChanged(CharacterDraft value)
    {
        if (_watchedDraft is not null)
        {
            _watchedDraft.Relations.CollectionChanged -= OnRelationsChanged;
        }

        _watchedDraft = value;
        value.Relations.CollectionChanged += OnRelationsChanged;

        foreach (var field in _fields)
        {
            field.Refresh();
        }

        RefreshParticipation();
        RefreshSidePanel();
    }

    private void OnRelationsChanged(object? sender, NotifyCollectionChangedEventArgs e) => RefreshSidePanel();

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

    /// <summary>
    /// Наполняет правую панель реальными данными проекта: связи карточки, линии с участием
    /// персонажа и число сцен, где он появляется (ТЗ п. 27; ADR 010).
    /// </summary>
    private void RefreshSidePanel()
    {
        RelatedCharacters.Clear();
        var index = 0;
        foreach (var relation in Draft.Relations)
        {
            if (relation.Character is not { } contact)
            {
                continue;
            }

            RelatedCharacters.Add(new RelatedCharacterEntry(
                InitialOf(contact.Name),
                contact.Name,
                relation.Type?.Label ?? StoryLabels.RelationTypes[0].Label,
                new SolidColorBrush(Color.Parse(RelatedAvatarColors[index % RelatedAvatarColors.Length]))));
            index++;
        }

        RelatedLines.Clear();
        var scenes = new HashSet<Guid>();
        if (!Draft.IsNew)
        {
            foreach (var line in _architecture.GetPlotLines())
            {
                var steps = line.Steps
                    .Where(step => step.Participants.Any(participant => participant.CharacterId == Draft.Id))
                    .ToList();
                if (steps.Count == 0)
                {
                    continue;
                }

                RelatedLines.Add(new RelatedLineEntry(
                    line.Name,
                    StoryLabels.BrushFromHex(line.ColorHex, StoryLabels.DefaultLineBrush),
                    StoryLabels.StatusDot(line.Status),
                    StoryLabels.LineStatusLabel(line.Status)));

                foreach (var id in line.SceneIds)
                {
                    scenes.Add(id);
                }

                foreach (var step in steps)
                {
                    foreach (var id in step.SceneIds)
                    {
                        scenes.Add(id);
                    }
                }
            }
        }

        SceneCount = scenes.Count;
        OnPropertyChanged(nameof(HasRelatedCharacters));
        OnPropertyChanged(nameof(HasRelatedLines));
    }

    private static string InitialOf(string name) =>
        string.IsNullOrWhiteSpace(name) ? "?" : name.Trim()[..1].ToUpperInvariant();

    /// <summary>Русское склонение слова «сцена» по числу.</summary>
    private static string SceneWord(int count)
    {
        var mod100 = count % 100;
        var mod10 = count % 10;

        if (mod100 is >= 11 and <= 14)
        {
            return "сцен";
        }

        return mod10 switch
        {
            1 => "сцена",
            2 or 3 or 4 => "сцены",
            _ => "сцен",
        };
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
        if (_isCharactersScreen && screen != AppScreen.Characters)
        {
            _ = CommitDraftAsync();
        }

        _isCharactersScreen = screen == AppScreen.Characters;
        if (screen == AppScreen.Characters)
        {
            Refresh();
        }
    }

    private void LoadDraft(Guid characterId)
    {
        var card = _architecture.GetCharacters().FirstOrDefault(character => character.Id == characterId);
        Draft = card is null ? CharacterDraft.New() : CharacterDraft.From(card, CharacterChoices);
        _savedSignature = Signature(Draft.ToDto());
    }

    private static string Signature(CharacterDto card) =>
        System.Text.Json.JsonSerializer.Serialize(card);

    /// <summary>
    /// Обновляет строку списка без замены выбранной карточки: фоновое сохранение
    /// не должно перетягивать выбор пользователя на только что сохранённого персонажа.
    /// </summary>
    private void UpdateListItem(CharacterDto saved)
    {
        var item = new CharacterListItem(
            saved.Id,
            saved.Name,
            StoryLabels.RoleLabel(saved.Role),
            StoryLabels.StatusLabel(saved.Status),
            saved.ArcStages.Count);
        for (var index = 0; index < Characters.Count; index++)
        {
            if (Characters[index].Id != saved.Id)
            {
                continue;
            }

            if (!ReferenceEquals(Characters[index], Selected))
            {
                Characters[index] = item;
            }

            return;
        }

        if (Selected?.Id != saved.Id)
        {
            Characters.Add(item);
            OnPropertyChanged(nameof(HasCharacters));
        }
    }

    private async Task ReportAsync(AppError error)
    {
        ErrorMessage = error.SuggestedAction is null
            ? Loc.T(error.UserMessage)
            : $"{Loc.T(error.UserMessage)} {Loc.T(error.SuggestedAction)}";
        await _dialogs.ShowErrorAsync(Loc.T("Ошибка персонажа"), ErrorMessage);
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
