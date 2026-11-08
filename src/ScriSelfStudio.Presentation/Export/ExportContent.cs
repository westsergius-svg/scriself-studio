namespace ScriSelfStudio.Presentation.Export;

using ScriSelfStudio.Contracts.Dtos;
using ScriSelfStudio.Core.Text;
using ScriSelfStudio.Presentation.Localization;

/// <summary>Вид абзаца в экспортируемой рукописи.</summary>
public enum ExportBlockKind
{
    Paragraph,
    Heading,
    Quote,
    Bullet,
    Numbered,
    Separator,
}

/// <summary>Фрагмент текста с одинаковым начертанием.</summary>
public sealed record ExportRun(
    string Text,
    bool Bold = false,
    bool Italic = false,
    bool Underline = false,
    bool Strikethrough = false);

/// <summary>Абзац рукописи, не зависящий от формата файла.</summary>
public sealed record ExportParagraph(
    ExportBlockKind Kind,
    IReadOnlyList<ExportRun> Runs,
    TextAlignment Alignment = TextAlignment.Left)
{
    public string PlainText => string.Concat(Runs.Select(run => run.Text));
}

public sealed record ExportScene(string Title, IReadOnlyList<ExportParagraph> Paragraphs);

public sealed record ExportChapter(string Title, IReadOnlyList<ExportScene> Scenes);

public sealed record ExportBook(string Title, IReadOnlyList<ExportChapter> Chapters);

public sealed record ExportCharacterField(string Label, string Value);

public sealed record ExportCharacter(string Name, IReadOnlyList<ExportCharacterField> Fields);

/// <summary>Готовая рукопись для DOCX, PDF, EPUB, TXT и Markdown.</summary>
public sealed record ExportManuscript(string Title, string Subtitle, IReadOnlyList<ExportBook> Books)
{
    public IReadOnlyList<ExportCharacter> Characters { get; init; } = [];
}

/// <summary>Настройки оформления, общие для всех форматов экспорта.</summary>
public sealed record ExportOptions(
    string FontFamily,
    double FontSize,
    double LineSpacing,
    bool IncludeTitlePage,
    bool TableOfContents,
    bool PageNumbering,
    bool IncludeSceneHeadings = false,
    bool IncludeCharacterCards = false);

/// <summary>
/// Собирает формат-нозависимую рукопись из структуры книги и документов сцен.
/// Форматирование фрагментов переносится в <see cref="ExportRun"/>; заголовки
/// остаются заголовками, а списки и цитаты — отдельными видами абзацев.
/// </summary>
public static class ManuscriptExportBuilder
{
    public static ExportManuscript Build(
        string title,
        IReadOnlyList<BookDto> books,
        Func<Guid, RichDocument?> readDocument,
        bool preserveFormatting,
        IReadOnlyList<CharacterDto>? characters = null)
    {
        var exportBooks = books
            .Select(book => new ExportBook(
                book.Title,
                book.Chapters
                    .Select(chapter => new ExportChapter(
                        chapter.Title,
                        chapter.Scenes
                            .Select(scene => new ExportScene(
                                scene.Title,
                                BuildParagraphs(readDocument(scene.Id), preserveFormatting)))
                            .ToList()))
                    .ToList()))
            .ToList();

        var subtitle = books.FirstOrDefault()?.Title ?? string.Empty;
        return new ExportManuscript(title, subtitle, exportBooks)
        {
            Characters = (characters ?? [])
                .OrderBy(character => character.Order)
                .Select(character => ToExportCharacter(character, characters ?? []))
                .ToList(),
        };
    }

    private static ExportCharacter ToExportCharacter(
        CharacterDto character,
        IReadOnlyList<CharacterDto> allCharacters)
    {
        var fields = new List<ExportCharacterField>();
        AddField(fields, "Псевдоним", character.Nickname);
        AddField(fields, "Роль", character.Role switch
        {
            ScriSelfStudio.Core.Model.CharacterRole.Protagonist => "Протагонист",
            ScriSelfStudio.Core.Model.CharacterRole.Antagonist => "Антагонист",
            ScriSelfStudio.Core.Model.CharacterRole.Ally => "Союзник",
            ScriSelfStudio.Core.Model.CharacterRole.Mentor => "Наставник",
            ScriSelfStudio.Core.Model.CharacterRole.Episodic => "Эпизодический",
            _ => "Второстепенный",
        });
        AddField(fields, "Важность", ImportanceLabel(character.Importance));
        AddField(fields, "Статус персонажа", StatusLabel(character.Status));
        AddField(fields, "Возраст", character.Age);
        AddField(fields, "Пол", character.Gender);
        AddField(fields, "Вид / раса", character.Species);
        AddField(fields, "Занятие", character.Occupation);
        AddField(fields, "Мир", character.World);
        AddField(fields, "Теги", character.Tags.Count == 0 ? null : string.Join(", ", character.Tags));
        AddField(fields, "Описание", character.Description);
        AddField(fields, "Внешность", character.Appearance);
        AddField(fields, "Внешность в состояниях", character.AppearanceStates);
        AddField(fields, "Голос", character.Voice);
        AddField(fields, "Жесты и привычки", character.Mannerisms);
        AddField(fields, "Референсы", character.References);
        AddField(fields, "Архетип", character.Archetype);
        AddField(fields, "Характер", character.Temperament);
        AddField(fields, "Ценности", character.Values);
        AddField(fields, "Убеждения", character.Beliefs);
        AddField(fields, "Ложное убеждение", character.FalseBelief);
        AddField(fields, "Внутренняя потребность", character.InternalNeed);
        AddField(fields, "Желание", character.Want);
        AddField(fields, "Страхи", character.Fears);
        AddField(fields, "Слабости", character.Weaknesses);
        AddField(fields, "Сильные стороны", character.Strengths);
        AddField(fields, "Привычки", character.Habits);
        AddField(fields, "Юмор", character.Humour);
        AddField(fields, "Защитные механизмы", character.DefenceMechanisms);
        AddField(fields, "Способы справляться", character.CopingStrategies);
        AddField(fields, "Мотивация", character.Motivation);
        AddField(fields, "Внешняя цель", character.ExternalGoal);
        AddField(fields, "Тайная цель", character.SecretGoal);
        AddField(fields, "Готов сделать", character.ReadyToDo);
        AddField(fields, "Никогда не сделает", character.NeverDo);
        AddField(fields, "Победа", character.Victory);
        AddField(fields, "Поражение", character.Failure);
        AddField(fields, "Образование", character.Education);
        AddField(fields, "Навыки", character.Skills);
        AddField(fields, "Бой", character.Combat);
        AddField(fields, "Способности", character.Powers);
        AddField(fields, "Таланты", character.Talents);
        AddField(fields, "Ограничения", character.Limitations);
        AddField(fields, "Инвентарь", character.Inventory);
        AddField(fields, "Спутники", character.Companions);
        AddField(fields, "Речь", character.Speech);
        AddField(fields, "Акцент", character.Accent);
        AddField(fields, "Речевые привычки", character.SpeechHabits);
        AddField(fields, "Избегает в речи", character.SpeechAvoided);
        AddField(fields, "Речь под стрессом", character.SpeechUnderStress);
        AddField(fields, "Место рождения", character.Birthplace);
        AddField(fields, "Детство", character.Childhood);
        AddField(fields, "Травма", character.Trauma);
        AddField(fields, "Карьера", character.Career);
        AddField(fields, "Поворотные моменты", character.TurningPoints);
        AddField(fields, "Начало истории", character.StoryStart);
        AddField(fields, "Хронология", character.Chronology);
        AddField(fields, "Тип арки", ArcTypeLabel(character.ArcType));
        AddField(fields, "Арка персонажа", character.ArcSummary);
        AddField(fields, "Секреты", character.Secrets);
        AddField(fields, "Секреты от себя", character.SecretsFromSelf);
        AddField(fields, "Кто хранит секрет", character.SecretKeepers);
        AddField(fields, "Раскрытие секрета", character.SecretReveal);
        AddField(fields, "Последствия секрета", character.SecretConsequences);
        AddField(fields, "Физические изменения", character.PhysicalChange);
        AddField(fields, "Психологические изменения", character.PsychologicalChange);
        AddField(fields, "Изменения отношений", character.RelationshipChange);
        AddField(fields, "Ключевые решения", character.KeyDecisions);
        AddField(fields, "Теряет", character.Loses);
        AddField(fields, "Обретает", character.Gains);
        AddField(fields, "Идеи сцен", character.SceneIdeas);
        AddField(fields, "Цитаты", character.Quotes);
        AddField(fields, "Открытые вопросы", character.OpenQuestions);
        AddField(fields, "Статус проработки", DraftStatusLabel(character.DraftStatus));
        AddField(fields, "Автор", character.Author);
        AddField(fields, "Заметки", character.Notes);

        var characterNames = allCharacters.ToDictionary(item => item.Id, item => item.Name);
        var relations = character.Relations
            .Select(relation =>
            {
                var name = characterNames.GetValueOrDefault(relation.CharacterId, Loc.T("Неизвестный персонаж"));
                var type = Loc.T(relation.Type switch
                {
                    ScriSelfStudio.Core.Model.CharacterRelationType.Family => "Семья",
                    ScriSelfStudio.Core.Model.CharacterRelationType.Friend => "Друг",
                    ScriSelfStudio.Core.Model.CharacterRelationType.Enemy => "Враг",
                    ScriSelfStudio.Core.Model.CharacterRelationType.Love => "Любовь",
                    ScriSelfStudio.Core.Model.CharacterRelationType.Mentor => "Наставник",
                    ScriSelfStudio.Core.Model.CharacterRelationType.Rival => "Соперник",
                    ScriSelfStudio.Core.Model.CharacterRelationType.Dependency => "Зависимость",
                    _ => "Другое",
                });
                var details = string.Join("; ", new[] { relation.Dynamic, relation.Status }
                    .Where(value => !string.IsNullOrWhiteSpace(value)));
                return string.IsNullOrWhiteSpace(details) ? $"{name} — {type}" : $"{name} — {type}: {details}";
            });
        AddField(fields, "Связи", string.Join("; ", relations));

        var stages = character.ArcStages
            .OrderBy(stage => stage.Order)
            .Select(stage => string.IsNullOrWhiteSpace(stage.Description)
                ? stage.Title
                : $"{stage.Title}: {stage.Description}");
        AddField(fields, "Этапы арки", string.Join("; ", stages));

        return new ExportCharacter(character.Name, fields);
    }

    private static string ImportanceLabel(ScriSelfStudio.Core.Model.CharacterImportance value) => Loc.T(value switch
    {
        ScriSelfStudio.Core.Model.CharacterImportance.Main => "Главный",
        ScriSelfStudio.Core.Model.CharacterImportance.Background => "Фоновый",
        _ => "Второстепенный",
    });

    private static string StatusLabel(ScriSelfStudio.Core.Model.CharacterStatus value) => Loc.T(value switch
    {
        ScriSelfStudio.Core.Model.CharacterStatus.Dead => "Погиб",
        ScriSelfStudio.Core.Model.CharacterStatus.Missing => "Пропал",
        ScriSelfStudio.Core.Model.CharacterStatus.Unknown => "Неизвестен",
        _ => "В работе",
    });

    private static string ArcTypeLabel(ScriSelfStudio.Core.Model.CharacterArcType value) => Loc.T(value switch
    {
        ScriSelfStudio.Core.Model.CharacterArcType.Negative => "Отрицательная",
        ScriSelfStudio.Core.Model.CharacterArcType.Flat => "Плоская",
        _ => "Положительная",
    });

    private static string DraftStatusLabel(ScriSelfStudio.Core.Model.CharacterDraftStatus value) => Loc.T(value switch
    {
        ScriSelfStudio.Core.Model.CharacterDraftStatus.Draft => "Черновик",
        ScriSelfStudio.Core.Model.CharacterDraftStatus.Ready => "Готово",
        _ => "Идея",
    });

    private static void AddField(List<ExportCharacterField> fields, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            fields.Add(new ExportCharacterField(Loc.T(label), value.Trim()));
        }
    }

    private static IReadOnlyList<ExportParagraph> BuildParagraphs(RichDocument? document, bool preserveFormatting)
    {
        if (document is null)
        {
            return [];
        }

        var paragraphs = new List<ExportParagraph>(document.Blocks.Count);
        foreach (var block in document.Blocks)
        {
            var kind = block.Kind switch
            {
                BlockKind.Heading1 or BlockKind.Heading2 or BlockKind.Heading3
                    or BlockKind.Heading4 or BlockKind.Heading5 or BlockKind.Heading6 => ExportBlockKind.Heading,
                BlockKind.Quote => ExportBlockKind.Quote,
                BlockKind.BulletListItem => ExportBlockKind.Bullet,
                BlockKind.NumberedListItem => ExportBlockKind.Numbered,
                BlockKind.Separator => ExportBlockKind.Separator,
                _ => ExportBlockKind.Paragraph,
            };

            if (kind == ExportBlockKind.Separator)
            {
                paragraphs.Add(new ExportParagraph(
                    kind,
                    [new ExportRun("* * *")],
                    TextAlignment.Center));
                continue;
            }

            var runs = new List<ExportRun>();
            foreach (var span in block.Spans)
            {
                if (span.Text.Length == 0)
                {
                    continue;
                }

                var flags = preserveFormatting ? span.Style.Flags : TextFormat.None;
                runs.Add(new ExportRun(
                    span.Text,
                    flags.HasFlag(TextFormat.Bold),
                    flags.HasFlag(TextFormat.Italic),
                    flags.HasFlag(TextFormat.Underline),
                    flags.HasFlag(TextFormat.Strikethrough)));
            }

            paragraphs.Add(new ExportParagraph(kind, runs, block.Alignment));
        }

        return paragraphs;
    }
}
