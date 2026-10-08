namespace ScriSelfStudio.Persistence.Formats;

using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using ScriSelfStudio.Contracts.Storage;
using ScriSelfStudio.Core.Model;
using ScriSelfStudio.Core.Text;

/// <summary>
/// Импорт раннего модульного формата <c>.scri</c>: SQLite с таблицами
/// <c>entities</c>/<c>relations</c>/<c>texts</c> вместо нашей схемы
/// (ADR 009, п. 6; ADR 010). Читает книгу, главы, сцены, тексты, персонажи,
/// сюжетные линии и их этапы в нашу модель.
/// </summary>
public static class LegacyModularImporter
{
    /// <summary>
    /// Идентификатор формата-источника. Ранние сборки этот формат не читали,
    /// поэтому при сохранении проект записывается в текущий формат ScriSelf Studio,
    /// а исходная версия остаётся в <c>&lt;файл&gt;.scri.bak</c>.
    /// </summary>
    public const string FormatId = "legacy-modular-sqlite";

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);

    /// <summary>Создан ли файл ранней модульной сборкой, а не нашим приложением.</summary>
    public static bool IsLegacyModularFile(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return false;
        }

        try
        {
            using var connection = OpenForInspection(filePath);
            var tables = ReadTables(connection);
            return tables.Contains("entities") && tables.Contains("meta");
        }
        catch (SqliteException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }


    /// <summary>
    /// Открывает legacy-файл только для чтения. Соединение намеренно не возвращается
    /// в пул: после импорта файл должен оставаться свободным для атомарной замены
    /// при первом сохранении проекта.
    /// </summary>
    internal static SqliteConnection OpenForInspection(string filePath)
    {
        var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = filePath,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false,
            }.ToString());
        connection.Open();
        return connection;
    }


    /// <summary>Читает ранний модульный проект в нашу модель.</summary>
    public static ProjectLoadResult Load(string filePath, DateTimeOffset now)
    {
        using var connection = OpenForInspection(filePath);

        var source = DescribeSource(ReadMeta(connection));
        var entities = ReadEntities(connection);
        var texts = ReadTexts(connection);
        var relations = ReadRelations(connection);

        var project = new Project
        {
            Id = Guid.NewGuid(),
            Name = source.ProjectName,
            Description = ComposeProjectDescription(source),
            SourceFormat = FormatId,
            Audit = { CreatedAt = source.CreatedAt, UpdatedAt = source.ModifiedAt },
        };

        var warnings = new List<string>
        {
            $"Проект импортирован из раннего модульного формата ({source.Descriptor}). "
            + "При сохранении файл будет записан в текущий формат ScriSelf Studio, "
            + "а исходная версия сохранится как <файл>.scri.bak.",
        };

        var bookEntity = entities.FirstOrDefault(entity => entity.Type == "book");
        var bookTitle = BookTitle(bookEntity) ?? project.Name;
        var series = new Series { Id = Guid.NewGuid(), ProjectId = project.Id, Title = bookTitle };
        var book = new Book
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            SeriesId = series.Id,
            Title = bookTitle,
            SourceId = bookEntity?.Id,
        };

        project.Series.Add(series);
        series.Books.Add(book);

        ImportChapters(entities, book);
        ImportScenes(entities, texts, book, warnings);
        ImportCharacters(entities, project, warnings);
        ImportCharacterSteps(entities, project);
        ImportPlotLines(entities, relations, project, warnings);
        ImportLocations(entities, project);
        ImportStoryObjects(entities, project);
        LinkStepParticipants(relations, project);
        LinkPlotLineScenes(relations, book, project);
        AddUnsupportedTypeWarning(entities, warnings);
        AddUnreadFieldWarning(entities, warnings);

        return new ProjectLoadResult
        {
            Project = project,
            SourceSchemaVersion = source.FormatVersion,
            Warnings = warnings,
        };
    }


    private static void ImportChapters(IReadOnlyList<LegacyEntity> entities, Book book)
    {
        foreach (var entity in entities.Where(entity => entity.Type == "chapter").OrderBy(entity => entity.Order))
        {
            book.Chapters.Add(new Chapter
            {
                Id = Guid.NewGuid(),
                BookId = book.Id,
                SourceId = entity.Id,
                Title = entity.Raw("title") ?? entity.Name ?? "Без названия",
                Description = entity.Raw("description"),
                Order = entity.Int("number") ?? entity.Order,
                Audit = new EntityAudit { CreatedAt = entity.CreatedAt, UpdatedAt = entity.UpdatedAt },
            });
        }
    }


    private static void ImportScenes(
        IReadOnlyList<LegacyEntity> entities,
        IReadOnlyDictionary<string, string> texts,
        Book book,
        List<string> warnings)
    {
        var bySourceId = book.Chapters
            .Where(chapter => chapter.SourceId is not null)
            .ToDictionary(chapter => chapter.SourceId!, StringComparer.Ordinal);

        var missingChapter = 0;
        var missingText = 0;
        var loose = new List<Scene>();

        foreach (var entity in entities.Where(entity => entity.Type == "scene").OrderBy(entity => entity.Order))
        {
            var chapterId = entity.Raw("chapterId");
            var chapter = chapterId is not null && bySourceId.TryGetValue(chapterId, out var found) ? found : null;
            if (chapter is null)
            {
                missingChapter++;
            }

            var textId = entity.Raw("textId");
            var body = textId is not null && texts.TryGetValue(textId, out var stored) ? stored : string.Empty;
            if (textId is not null && body.Length == 0)
            {
                missingText++;
            }

            var scene = new Scene
            {
                Id = Guid.NewGuid(),
                ChapterId = chapter?.Id ?? Guid.Empty,
                SourceId = entity.Id,
                Title = entity.Raw("title") ?? entity.Name ?? "Сцена",
                Description = ComposeSceneDescription(entity),
                Status = MapStatus(entity.Raw("status")),
                Order = entity.Order,
                Document = ToDocument(body),
                Metadata = new SceneMetadata { IsKey = entity.Bool("isKey") },
                Audit = new EntityAudit { CreatedAt = entity.CreatedAt, UpdatedAt = entity.UpdatedAt },
            };

            (chapter is null ? loose : chapter.Scenes).Add(scene);
        }

        if (loose.Count > 0)
        {
            var placeholder = new Chapter { Id = Guid.NewGuid(), BookId = book.Id, Title = "Без главы" };
            foreach (var scene in loose)
            {
                scene.ChapterId = placeholder.Id;
            }

            placeholder.Scenes.AddRange(loose);
            book.Chapters.Add(placeholder);
        }

        if (missingChapter > 0)
        {
            warnings.Add($"Сцен без главы: {missingChapter}. Они собраны в главу \"Без главы\".");
        }

        if (missingText > 0)
        {
            warnings.Add($"Сцен без текста: {missingText}. Они открыты с пустым документом.");
        }
    }


    /// <summary>
    /// Линии раннего формата. В части сборок они описаны сущностью <c>plotline</c>,
    /// а в части — только идентификаторами в <c>plotline_stage.plotlineId</c> и в
    /// связях <c>scene_plotline.to_id</c>. Поэтому линия создаётся и для тех
    /// идентификаторов, у которых сущности нет, иначе её этапы и сцены потерялись бы.
    /// </summary>
    private static void ImportPlotLines(
        IReadOnlyList<LegacyEntity> entities,
        IReadOnlyList<LegacyRelation> relations,
        Project project,
        List<string> warnings)
    {
        var stepsByLine = entities
            .Where(entity => entity.Type == "plotline_stage")
            .GroupBy(entity => entity.Raw("plotlineId") ?? string.Empty, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.OrderBy(entity => entity.Order).ToList(), StringComparer.Ordinal);

        var totalSteps = stepsByLine.Values.Sum(items => items.Count);

        var descriptors = new List<(string SourceId, string Name, string? Description, string? Color, string? Status, int Order, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt)>();
        var known = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entity in entities.Where(entity => entity.Type == "plotline").OrderBy(entity => entity.Order))
        {
            known.Add(entity.Id);
            descriptors.Add((
                entity.Id,
                entity.Raw("name") ?? entity.Name ?? "Линия",
                entity.Raw("description"),
                entity.Raw("color"),
                entity.Raw("status"),
                entity.Order,
                entity.CreatedAt,
                entity.UpdatedAt));
        }

        // В части сборок роль сюжетной линии играет сущность story_arc: отдельных
        // plotline там нет, а этапы ссылаются на идентификаторы арок. Если же линии
        // в файле уже были, story_arc — это другое понятие (арки книги), и переносить
        // их в линии нельзя: пользователь получил бы дубликаты.
        var arcs = entities.Where(entity => entity.Type == "story_arc").OrderBy(entity => entity.Order).ToList();
        if (descriptors.Count == 0)
        {
            foreach (var entity in arcs)
            {
                known.Add(entity.Id);
                descriptors.Add((
                    entity.Id,
                    entity.Raw("title") ?? entity.Raw("name") ?? entity.Name ?? "Линия",
                    entity.Raw("description"),
                    entity.Raw("color"),
                    entity.Raw("status"),
                    entity.Order,
                    entity.CreatedAt,
                    entity.UpdatedAt));
            }
        }
        else if (arcs.Count > 0)
        {
            warnings.Add(
                $"Арки книги (story_arc): {arcs.Count}. В файле есть отдельные сюжетные линии, "
                + "поэтому арки не перенесены как линии — они остаются в файле-оригинале .scri.bak.");
        }

        // Линии, известные только по ссылкам: сортируются стабильно, чтобы порядок
        // не зависел от порядка строк в таблице.
        var referenced = stepsByLine.Keys
            .Concat(relations.Where(relation => relation.Type == "scene_plotline").Select(relation => relation.ToId))
            .Where(id => id.Length > 0 && !known.Contains(id))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        foreach (var id in referenced)
        {
            descriptors.Add((id, "Линия " + id, null, null, null, descriptors.Count, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
            warnings.Add($"Сюжетная линия «{id}» была задана только ссылками, без описания: имя восстановлено по идентификатору.");
        }

        foreach (var (sourceId, name, description, color, status, order, createdAt, updatedAt) in descriptors)
        {
            var line = new PlotLine
            {
                Id = Guid.NewGuid(),
                ProjectId = project.Id,
                SourceId = sourceId,
                Name = name,
                Description = description,
                ColorHex = color,
                Status = MapPlotLineStatus(status),
                Order = order,
                Audit = new EntityAudit { CreatedAt = createdAt, UpdatedAt = updatedAt },
            };

            if (stepsByLine.TryGetValue(sourceId, out var lineSteps))
            {
                foreach (var stepEntity in lineSteps)
                {
                    var step = new PlotLineStep
                    {
                        Id = Guid.NewGuid(),
                        PlotLineId = line.Id,
                        SourceId = stepEntity.Id,
                        Title = stepEntity.Raw("name") ?? stepEntity.Name ?? "Шаг",
                        Description = stepEntity.Raw("description"),
                        Icon = stepEntity.Raw("icon"),
                        IsFinal = stepEntity.Bool("isFinal"),
                        Order = stepEntity.Order,
                        Audit = new EntityAudit
                        {
                            CreatedAt = stepEntity.CreatedAt,
                            UpdatedAt = stepEntity.UpdatedAt,
                        },
                    };

                    ImportStepParticipants(stepEntity, step, project);
                    line.Steps.Add(step);
                }
            }

            project.PlotLines.Add(line);
        }

        var imported = project.PlotLines.Sum(line => line.Steps.Count);
        if (totalSteps > imported)
        {
            warnings.Add(
                $"Шагов сюжетных линий: {totalSteps}, перенесено {imported}. "
                + "Шаги, у которых не найдена своя линия, не перенесены.");
        }
    }


    private static void ImportCharacters(IReadOnlyList<LegacyEntity> entities, Project project, List<string> warnings)
    {
        var characters = entities.Where(entity => entity.Type == "character").OrderBy(entity => entity.Order).ToList();
        foreach (var entity in characters)
        {
            var character = new Character
            {
                Id = Guid.NewGuid(),
                ProjectId = project.Id,
                SourceId = entity.Id,
                Name = entity.Raw("name") ?? entity.Name ?? "Персонаж",
                Description = entity.Raw("note") ?? entity.Raw("description"),
                Motivation = entity.Raw("motivation"),
                Role = MapRole(entity.Raw("role")),
                Order = entity.Order,
                Audit = new EntityAudit { CreatedAt = entity.CreatedAt, UpdatedAt = entity.UpdatedAt },
            };

            foreach (var tag in ReadTags(entity.Raw("tags")))
            {
                character.Tags.Add(tag);
            }

            project.Characters.Add(character);
        }

        if (characters.Count > 0 && characters.All(entity => (entity.Data?.Count ?? 0) <= 3))
        {
            warnings.Add(
                "Ранний формат хранил у персонажа лишь имя, мотивацию и заметку: остальных полей "
                + "карточки в нём не было, поэтому они остаются пустыми.");
        }
    }


    /// <summary>Этапы личной арки персонажа (<c>character_stage</c>) из поздних сборок.</summary>
    private static void ImportCharacterSteps(IReadOnlyList<LegacyEntity> entities, Project project)
    {
        var steps = entities.Where(entity => entity.Type == "character_stage").OrderBy(entity => entity.Order).ToList();
        if (steps.Count == 0)
        {
            return;
        }

        var bySourceId = project.Characters
            .Where(character => character.SourceId is not null)
            .ToDictionary(character => character.SourceId!, StringComparer.Ordinal);

        for (var index = 0; index < steps.Count; index++)
        {
            var entity = steps[index];
            var characterSourceId = entity.Raw("characterId");
            if (characterSourceId is null || !bySourceId.TryGetValue(characterSourceId, out var character))
            {
                continue;
            }

            var step = new CharacterArcStage
            {
                Id = Guid.NewGuid(),
                CharacterId = character.Id,
                SourceId = entity.Id,
                Kind = MapArcStageKind(entity.Int("order") ?? index),
                Title = entity.Raw("name") ?? entity.Name ?? "Шаг",
                Description = entity.Raw("description"),
                Order = entity.Order,
                Audit = new EntityAudit { CreatedAt = entity.CreatedAt, UpdatedAt = entity.UpdatedAt },
            };

            character.ArcStages.Add(step);
        }
    }

    /// <summary>Локации раннего формата (библиотека мест действия).</summary>
    private static void ImportLocations(IReadOnlyList<LegacyEntity> entities, Project project)
    {
        foreach (var entity in entities.Where(entity => entity.Type == "location").OrderBy(entity => entity.Order))
        {
            project.Locations.Add(new Location
            {
                Id = Guid.NewGuid(),
                ProjectId = project.Id,
                SourceId = entity.Id,
                Name = entity.Raw("name") ?? entity.Name ?? "Локация",
                Description = entity.Raw("description"),
                Notes = entity.Raw("notes"),
                Order = entity.Order,
                Audit = new EntityAudit { CreatedAt = entity.CreatedAt, UpdatedAt = entity.UpdatedAt },
            });
        }
    }

    /// <summary>Значимые предметы истории раннего формата.</summary>
    private static void ImportStoryObjects(IReadOnlyList<LegacyEntity> entities, Project project)
    {
        foreach (var entity in entities.Where(entity => entity.Type == "object").OrderBy(entity => entity.Order))
        {
            project.StoryObjects.Add(new StoryObject
            {
                Id = Guid.NewGuid(),
                ProjectId = project.Id,
                SourceId = entity.Id,
                Name = entity.Raw("name") ?? entity.Name ?? "Предмет",
                Description = entity.Raw("description"),
                Notes = entity.Raw("notes"),
                Order = entity.Order,
                Audit = new EntityAudit { CreatedAt = entity.CreatedAt, UpdatedAt = entity.UpdatedAt },
            });
        }
    }

    /// <summary>
    /// Участники этапа: в одном поколении они лежат в JSON-поле <c>participants</c>
    /// (<c>{characterId, characterArcStageId}</c>), в другом — связями <c>stage_participant</c>.
    /// </summary>
    private static void ImportStepParticipants(LegacyEntity stepEntity, PlotLineStep step, Project project)
    {
        var raw = stepEntity.Raw("participants");
        if (string.IsNullOrWhiteSpace(raw))
        {
            return;
        }

        try
        {
            if (JsonNode.Parse(raw) is not JsonArray array)
            {
                return;
            }

            foreach (var item in array)
            {
                if (item is not JsonObject participant)
                {
                    continue;
                }

                AddParticipant(project, step, Text(participant, "characterId"), Text(participant, "characterArcStepId"));
            }
        }
        catch (JsonException)
        {
            // Повреждённый список участников не должен отменять импорт этапа.
        }
    }

    /// <summary>Связи <c>stage_participant</c>: персонаж и шаг линии в одной записи.</summary>
    private static void LinkStepParticipants(IReadOnlyList<LegacyRelation> relations, Project project)
    {
        foreach (var relation in relations.Where(relation => relation.Type == "stage_participant"))
        {
            var character = FindCharacterBySourceId(project, relation.FromId)
                ?? FindCharacterBySourceId(project, relation.ToId);
            var step = FindStepBySourceId(project, relation.ToId)
                ?? FindStepBySourceId(project, relation.FromId);

            if (character is null || step is null)
            {
                continue;
            }

            AddParticipant(project, step, character.SourceId, relation.Raw("characterArcStepId"));
        }
    }

    private static void AddParticipant(Project project, PlotLineStep step, string? characterSourceId, string? arcStageSourceId)
    {
        if (characterSourceId is null)
        {
            return;
        }

        var character = FindCharacterBySourceId(project, characterSourceId);
        if (character is null || step.FindParticipant(character.Id) is not null)
        {
            return;
        }

        var arcStage = arcStageSourceId is null
            ? null
            : character.ArcStages.FirstOrDefault(item => string.Equals(item.SourceId, arcStageSourceId, StringComparison.Ordinal));

        step.Participants.Add(new StepParticipant
        {
            CharacterId = character.Id,
            CharacterArcStageId = arcStage?.Id,
        });
    }

    private static Character? FindCharacterBySourceId(Project project, string sourceId) =>
        project.Characters.FirstOrDefault(character => string.Equals(character.SourceId, sourceId, StringComparison.Ordinal));

    private static PlotLineStep? FindStepBySourceId(Project project, string sourceId) =>
        project.PlotLines.SelectMany(line => line.Steps)
            .FirstOrDefault(step => string.Equals(step.SourceId, sourceId, StringComparison.Ordinal));

    /// <summary>
    /// Связи <c>scene_plotline</c>: сцена берётся из <c>from_id</c>, шаг — из
    /// <c>stepId</c> в метаданных связи. Сцена указывается и на этапе, и на самой
    /// линии, поэтому заполняются оба списка.
    /// </summary>
    private static void LinkPlotLineScenes(
        IReadOnlyList<LegacyRelation> relations,
        Book book,
        Project project)
    {
        var scenesBySourceId = book.Chapters
            .SelectMany(chapter => chapter.Scenes)
            .Where(scene => scene.SourceId is not null)
            .ToDictionary(scene => scene.SourceId!, StringComparer.Ordinal);

        var stepsBySourceId = project.PlotLines
            .SelectMany(line => line.Steps)
            .Where(step => step.SourceId is not null)
            .ToDictionary(step => step.SourceId!, StringComparer.Ordinal);

        var linesBySourceId = project.PlotLines
            .Where(line => line.SourceId is not null)
            .ToDictionary(line => line.SourceId!, StringComparer.Ordinal);

        foreach (var relation in relations.Where(relation => relation.Type == "scene_plotline"))
        {
            if (!scenesBySourceId.TryGetValue(relation.FromId, out var scene))
            {
                continue;
            }

            // Этап указан в метаданных связи; линия — в to_id. Этап приоритетнее,
            // но линия нужна и сама по себе, если этапа нет.
            var stepId = relation.Raw("stepId");
            PlotLine? line = null;

            if (stepId is not null && stepsBySourceId.TryGetValue(stepId, out var step))
            {
                line = project.FindPlotLine(step.PlotLineId);
                if (line is not null && !step.SceneIds.Contains(scene.Id))
                {
                    step.SceneIds.Add(scene.Id);
                }
            }

            if (line is null && linesBySourceId.TryGetValue(relation.ToId, out var linked))
            {
                line = linked;
                var firstStep = line.Steps.FirstOrDefault();
                if (firstStep is not null && !firstStep.SceneIds.Contains(scene.Id))
                {
                    firstStep.SceneIds.Add(scene.Id);
                }
            }

            if (line is not null && !line.SceneIds.Contains(scene.Id))
            {
                line.SceneIds.Add(scene.Id);
            }
        }
    }

    private static readonly HashSet<string> SupportedTypes = new(StringComparer.Ordinal)
    {
        "book", "chapter", "scene", "character", "character_stage",
        "plotline", "plotline_stage", "story_arc", "location", "object",
    };

    /// <summary>Предупреждает о типах сущностей, которых нет в текущей модели.</summary>
    private static void AddUnsupportedTypeWarning(IReadOnlyList<LegacyEntity> entities, List<string> warnings)
    {
        var unsupported = entities
            .Where(entity => !SupportedTypes.Contains(entity.Type))
            .Select(entity => entity.Type)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(type => type, StringComparer.Ordinal)
            .ToArray();

        if (unsupported.Length > 0)
        {
            warnings.Add(
                "В исходном файле были сущности, которых нет в текущей модели, — они не перенесены: "
                + string.Join(", ", unsupported) + ".");
        }
    }

    /// <summary>
    /// Отчёт о полях <c>data</c>, к которым импортёр не обращался. Список считается
    /// по факту чтения, а не по заранее заданному вручную списку: если в следующей
    /// сборке раннего приложения появится новое поле, оно попадёт сюда само, и
    /// пользователь увидит в строке состояния, что это поле не перенесено, а не
    /// потеряет его молча (ТЗ п. 12).
    /// </summary>
    private static void AddUnreadFieldWarning(IReadOnlyList<LegacyEntity> entities, List<string> warnings)
    {
        var byType = entities
            .Where(entity => SupportedTypes.Contains(entity.Type))
            .SelectMany(entity => entity.UnreadKeys().Select(key => (entity.Type, Key: key)))
            .GroupBy(item => item.Type, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal);

        var parts = byType
            .Select(group =>
            {
                var keys = group.Select(item => item.Key).Distinct(StringComparer.Ordinal)
                    .OrderBy(key => key, StringComparer.Ordinal);
                return $"{group.Key}: {string.Join(", ", keys)}";
            })
            .ToArray();

        if (parts.Length > 0)
        {
            warnings.Add(
                "Поля исходного формата, которые не перенесены (в текущей модели их нет): "
                + string.Join("; ", parts)
                + ". Значения сохранены в файле-оригинале .scri.bak.");
        }
    }


    private static IEnumerable<string> ReadTags(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            yield break;
        }

        foreach (var tag in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            yield return tag;
        }
    }

    private static string? ComposeProjectDescription(LegacySource source)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(source.Author))
        {
            parts.Add($"Автор: {source.Author}");
        }

        return parts.Count == 0 ? null : string.Join("\n", parts);
    }

    /// <summary>
    /// Описание сцены раннего формата собирается из <c>description</c>, <c>goal</c>,
    /// <c>conflict</c>, <c>result</c> и <c>dateTime</c>: отдельного поля описания
    /// в той сборке не было, а данные терять нельзя (ТЗ п. 12).
    /// </summary>
    private static string? ComposeSceneDescription(LegacyEntity entity)
    {
        var parts = new List<string>();
        AppendPart(parts, entity.Raw("description"), null);
        AppendPart(parts, entity.Raw("goal"), "Цель");
        AppendPart(parts, entity.Raw("conflict"), "Конфликт");
        AppendPart(parts, entity.Raw("result"), "Результат");
        AppendPart(parts, entity.Raw("dateTime"), "Время");
        return parts.Count == 0 ? null : string.Join("\n", parts);

        static void AppendPart(List<string> parts, string? value, string? label)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            parts.Add(label is null ? value.Trim() : $"{label}: {value.Trim()}");
        }
    }


    /// <summary>
    /// Плоский текст раннего формата превращается в абзацы документа: перевод строки
    /// в исходнике был границей абзаца, а оформления в нём не было.
    /// </summary>
    private static RichDocument ToDocument(string text)
    {
        var document = new RichDocument();
        if (text.Length == 0)
        {
            document.Blocks.Add(new TextBlock());
            return document;
        }

        foreach (var line in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            var block = new TextBlock();
            if (line.Length > 0)
            {
                block.Spans.Add(new TextSpan { Text = line });
            }

            document.Blocks.Add(block);
        }

        if (document.Blocks.Count == 0)
        {
            document.Blocks.Add(new TextBlock());
        }

        return document;
    }

    private static string? BookTitle(LegacyEntity? book)
    {
        if (book is null)
        {
            return null;
        }

        var title = book.Raw("title") ?? book.Name;
        return string.IsNullOrWhiteSpace(title) ? null : title;
    }

    private static SceneStatus MapStatus(string? status) => status switch
    {
        "in_progress" or "wip" or "draft" or "writing" => SceneStatus.Draft,
        "revised" or "edited" or "review" => SceneStatus.Revised,
        "done" or "final" or "finished" or "completed" or "published" => SceneStatus.Final,
        _ => SceneStatus.Idea,
    };

    private static CharacterRole MapRole(string? role) => role switch
    {
        "protagonist" or "hero" or "main" => CharacterRole.Protagonist,
        "antagonist" or "villain" => CharacterRole.Antagonist,
        "ally" or "friend" => CharacterRole.Ally,
        "mentor" or "teacher" => CharacterRole.Mentor,
        "episodic" => CharacterRole.Episodic,
        _ => CharacterRole.Secondary,
    };

    /// <summary>Статус линии из раннего формата: неизвестное значение — «В работе».</summary>
    private static PlotLineStatus MapPlotLineStatus(string? status) => status?.Trim().ToLowerInvariant() switch
    {
        "idea" or "concept" => PlotLineStatus.Idea,
        "draft" or "outline" => PlotLineStatus.Draft,
        "done" or "final" or "completed" or "complete" or "finished" => PlotLineStatus.Done,
        _ => PlotLineStatus.InProgress,
    };

    /// <summary>Порядковый этап арки приводится к каноническим шагам; лишние — «свой шаг».</summary>
    private static CharacterArcStageKind MapArcStageKind(int order) => order switch
    {
        0 => CharacterArcStageKind.InitialState,
        1 => CharacterArcStageKind.Trigger,
        2 => CharacterArcStageKind.Resistance,
        3 => CharacterArcStageKind.TurningPoint,
        4 => CharacterArcStageKind.Crisis,
        5 => CharacterArcStageKind.Realization,
        6 => CharacterArcStageKind.FinalState,
        _ => CharacterArcStageKind.Custom,
    };

    private static string? Text(JsonObject? data, string key)
    {
        if (data is null || !data.TryGetPropertyValue(key, out var node) || node is null)
        {
            return null;
        }

        try
        {
            return node.GetValueKind() switch
            {
                JsonValueKind.String => node.GetValue<string>(),
                JsonValueKind.Null or JsonValueKind.Undefined => null,
                _ => node.ToJsonString().Trim('"'),
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }


    private static HashSet<string> ReadTables(SqliteConnection connection)
    {
        var tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type='table';";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            tables.Add(reader.GetString(0));
        }

        return tables;
    }

    private static Dictionary<string, string> ReadMeta(SqliteConnection connection)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT key, value FROM meta;";
        using var reader = command.ExecuteReader();
        while (reader.Read() && !reader.IsDBNull(0) && !reader.IsDBNull(1))
        {
            result[reader.GetString(0)] = reader.GetString(1);
        }

        return result;
    }

    /// <summary>Повреждённый JSON в одном поле не должен отменять импорт всего файла.</summary>
    private static JsonObject? ParseObject(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(payload) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static List<LegacyEntity> ReadEntities(SqliteConnection connection)
    {
        var result = new List<LegacyEntity>();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT id, type, data, name, ord, created_at, updated_at FROM entities ORDER BY ord, id;";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new LegacyEntity
            {
                Id = reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
                Type = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                Data = ParseObject(reader.IsDBNull(2) ? null : reader.GetString(2)),
                Name = reader.IsDBNull(3) ? null : reader.GetString(3),
                Order = reader.IsDBNull(4) ? 0 : (int)Math.Round(reader.GetDouble(4)),
                CreatedAt = ParseTimestamp(reader.IsDBNull(5) ? null : reader.GetString(5)),
                UpdatedAt = ParseTimestamp(reader.IsDBNull(6) ? null : reader.GetString(6)),
            });
        }

        return result;
    }


    private static Dictionary<string, string> ReadTexts(SqliteConnection connection)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var hasFormat = ColumnExists(connection, "texts", "format");

        using var command = connection.CreateCommand();
        command.CommandText = hasFormat
            ? "SELECT id, content, compressed, format FROM texts;"
            : "SELECT id, content, compressed FROM texts;";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (reader.IsDBNull(0))
            {
                continue;
            }

            var compressed = !reader.IsDBNull(2) && reader.GetInt64(2) != 0;
            var stored = reader.IsDBNull(1)
                ? string.Empty
                : reader.GetValue(1) switch
                {
                    byte[] bytes => DecodeText(bytes, compressed),
                    string text => text,
                    _ => string.Empty,
                };

            var format = hasFormat && !reader.IsDBNull(3) ? reader.GetString(3) : null;
            result[reader.GetString(0)] = ToPlainText(stored, format);
        }

        return result;
    }

    private static bool ColumnExists(SqliteConnection connection, string table, string column)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({table});";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (reader.FieldCount > 1 && string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Поздние сборки хранят текст с флагом <c>format</c>: значение <c>rich</c> — это
    /// JSON с блоками и «прогонами». В новой версии редактор плоский, поэтому из
    /// разметки берётся только текст (отказ от собственного текстового движка).
    /// </summary>
    private static string ToPlainText(string stored, string? format) =>
        string.Equals(format, "rich", StringComparison.OrdinalIgnoreCase)
            ? RichJsonToPlainText(stored)
            : stored;

    private static string RichJsonToPlainText(string json)
    {
        try
        {
            if (JsonNode.Parse(json) is not JsonObject root || root["blocks"] is not JsonArray blocks)
            {
                return json;
            }

            var lines = new List<string>();
            foreach (var node in blocks)
            {
                var builder = new StringBuilder();
                if (node is JsonObject block && block["runs"] is JsonArray runs)
                {
                    foreach (var runNode in runs)
                    {
                        if (runNode is JsonObject run && run["text"] is JsonValue textValue &&
                            textValue.TryGetValue<string>(out var runText))
                        {
                            builder.Append(runText);
                        }
                    }
                }

                lines.Add(builder.ToString());
            }

            return lines.Count == 0 ? string.Empty : string.Join("\n", lines);
        }
        catch (JsonException)
        {
            return json;
        }
    }

    private static string DecodeText(byte[] bytes, bool compressed)
    {
        if (!compressed)
        {
            return Utf8.GetString(bytes);
        }

        // Сжатые тексты во встречающихся файлах используют deflate; если формат другой,
        // содержимое читается как есть — лучше текст с артефактами, чем пустота.
        try
        {
            using var input = new MemoryStream(bytes, writable: false);
            using var deflate = new DeflateStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            deflate.CopyTo(output);
            return Utf8.GetString(output.ToArray());
        }
        catch (InvalidDataException)
        {
            return Utf8.GetString(bytes);
        }
    }

    private static List<LegacyRelation> ReadRelations(SqliteConnection connection)
    {
        var result = new List<LegacyRelation>();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT from_id, to_id, rel_type, meta FROM relations;";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new LegacyRelation
            {
                FromId = reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
                ToId = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                Type = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                Meta = ParseObject(reader.IsDBNull(3) ? null : reader.GetString(3)),
            });
        }

        return result;
    }


    private static LegacySource DescribeSource(Dictionary<string, string> meta)
    {
        var appVersion = Get(meta, "appVersion");
        var formatVersion = Get(meta, "formatVersion");
        var descriptor = appVersion ?? (formatVersion is null ? "модульный" : $"formatVersion {formatVersion}");

        return new LegacySource
        {
            Descriptor = descriptor,
            ProjectName = Get(meta, "title") ?? "Импортированный проект",
            Author = Get(meta, "author"),
            FormatVersion = int.TryParse(formatVersion, NumberStyles.Integer, CultureInfo.InvariantCulture, out var version)
                ? version
                : 0,
            CreatedAt = ParseTimestamp(Get(meta, "createdAt")),
            ModifiedAt = ParseTimestamp(Get(meta, "modifiedAt") ?? Get(meta, "updatedAt")),
        };
    }

    private static string? Get(Dictionary<string, string> meta, string key) =>
        meta.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    /// <summary>
    /// Метка времени раннего формата: ISO-8601 либо Unix-секунды — в самой ранней
    /// сборке <c>createdAt</c> и <c>updatedAt</c> хранились числами.
    /// </summary>
    private static DateTimeOffset ParseTimestamp(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return DateTimeOffset.UtcNow;
        }

        if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
        {
            return parsed.ToUniversalTime();
        }

        return long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds)
               && seconds is > 0 and < 4_000_000_000
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : DateTimeOffset.UtcNow;
    }

    /// <summary>Сущность раннего формата: общие поля плюс произвольный JSON в <c>data</c>.</summary>
    private sealed class LegacyEntity
    {
        private readonly HashSet<string> _readKeys = new(StringComparer.Ordinal);

        public required string Id { get; init; }

        public required string Type { get; init; }

        public JsonObject? Data { get; init; }

        public string? Name { get; init; }

        public int Order { get; init; }

        public DateTimeOffset CreatedAt { get; init; }

        public DateTimeOffset UpdatedAt { get; init; }

        /// <summary>
        /// Читает поле <c>data</c> и запоминает, что оно было запрошено. Список
        /// нужен для отчёта о полях, которые мы не переносим (см. <see cref="AddUnreadFieldWarning"/>).
        /// </summary>
        public string? Raw(string key)
        {
            _readKeys.Add(key);
            return Text(Data, key);
        }

        /// <summary>Ключи <c>data</c>, к которым импортёр не обращался.</summary>
        public IEnumerable<string> UnreadKeys()
        {
            if (Data is null)
            {
                return [];
            }

            return Data.Select(pair => pair.Key).Where(key => !_readKeys.Contains(key));
        }

        public int? Int(string key) =>
            int.TryParse(Raw(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
                ? value
                : null;

        public bool Bool(string key) => Raw(key) is "true" or "1" or "yes";
    }

    private sealed class LegacyRelation
    {
        public required string FromId { get; init; }

        public required string ToId { get; init; }

        public required string Type { get; init; }

        public JsonObject? Meta { get; init; }

        public string? Raw(string key) => Text(Meta, key);
    }

    private sealed class LegacySource
    {
        public required string Descriptor { get; init; }

        public required string ProjectName { get; init; }

        public string? Author { get; init; }

        public int FormatVersion { get; init; }

        public DateTimeOffset CreatedAt { get; init; }

        public DateTimeOffset ModifiedAt { get; init; }
    }
}