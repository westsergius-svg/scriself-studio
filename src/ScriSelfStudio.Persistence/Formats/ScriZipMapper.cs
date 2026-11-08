namespace ScriSelfStudio.Persistence.Formats;

using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using ScriSelfStudio.Core.Json;
using ScriSelfStudio.Core.Model;
using ScriSelfStudio.Core.Text;

/// <summary>
/// Преобразование между нашей моделью проекта и старым ZIP-форматом
/// (<c>manifest.json</c> + <c>content.json</c>).
///
/// Переносятся книга, главы, сцены, тексты, статусы и сюжетные линии. Поля, которых нет
/// в нашей модели (автор, жанр, персонажи, локации, объекты, тома, служебные поля глав),
/// не выбрасываются: они остаются в исходном документе и записываются обратно как были.
/// </summary>
public static class ScriZipMapper
{
    public const string DefaultSeriesTitle = "Основная";

    /// <summary>Наш раздел в <c>content.json</c>: старые версии его не читают.</summary>
    private const string ExtrasKey = "scriself";

    /// <summary>
    /// Версия нашего раздела <c>scriself</c>. Это версия <b>наших расширений</b>,
    /// а не старого формата: <c>manifest.version</c> остаётся 1, потому что
    /// <c>content.json</c> мы не меняем. Прежние версии приложения этот раздел
    /// не читают вовсе, поэтому цифра ничего для них не значит.
    /// </summary>
    /// <remarks>
    /// При чтении версия намеренно не проверяется: каждый ключ раздела
    /// (<c>characters</c>, <c>plotlines</c>, <c>documents</c>) читается по отдельности
    /// и отсутствие или порча одного не мешает остальным. Строгая проверка здесь
    /// означала бы, что файл, записанный более новой версией, вообще не открылся бы,
    /// хотя читать его мы умеем. Новый ключ всегда аддитивен и появляется без
    /// изменения этой цифры; её стоит поднять только на несовместимое изменение
    /// уже записанных ключей, и тогда — вместе с проверкой при чтении.
    /// </remarks>
    private const int ExtrasVersion = 1;

    private const string LegacySceneStatusIdea = "idea";
    private const string LegacySceneStatusDraft = "draft";
    private const string LegacySceneStatusInProgress = "in_progress";
    private const string LegacySceneStatusFinal = "final";

    public static Project ToProject(JsonObject manifest, JsonObject content, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(content);

        var book = content["book"] as JsonObject;
        var name = FirstNonEmpty(Text(manifest, "name"), Text(book, "title"), "Без названия");

        var project = new Project
        {
            Name = name,
            Description = NullIfEmpty(Text(book, "synopsis")),
            SourceFormat = ScriZipFormat.FormatId,
            Audit = new EntityAudit
            {
                CreatedAt = ParseTime(Text(manifest, "createdAt"), now),
                UpdatedAt = ParseTime(Text(manifest, "updatedAt"), now),
            },
        };

        var series = new Series { Title = DefaultSeriesTitle, Order = 0, ProjectId = project.Id };
        var legacyBook = new Book
        {
            Title = FirstNonEmpty(Text(book, "title"), project.Name),
            Order = 0,
            SeriesId = series.Id,
            ProjectId = project.Id,
            SourceId = "book",
        };

        series.Books.Add(legacyBook);
        project.Series.Add(series);

        var scenesByPlotline = new Dictionary<string, List<Guid>>(StringComparer.Ordinal);
        var chapters = content["chapters"] as JsonArray ?? [];
        var chapterOrder = 0;
        foreach (var chapterNode in chapters.OfType<JsonObject>())
        {
            var chapter = new Chapter
            {
                Title = FirstNonEmpty(Text(chapterNode, "title"), $"Глава {chapterOrder + 1}"),
                Description = NullIfEmpty(Text(chapterNode, "synopsis")),
                Order = chapterOrder,
                BookId = legacyBook.Id,
                SourceId = NullIfEmpty(Text(chapterNode, "id")),
            };

            var sceneNodes = (chapterNode["scenes"] as JsonArray ?? []).OfType<JsonObject>().ToList();
            var hasSceneText = sceneNodes.Any(node => NullIfEmpty(Text(node, "text")) is not null);
            var sceneOrder = 0;
            foreach (var sceneNode in sceneNodes)
            {
                var document = PlainTextDocument.FromText(Text(sceneNode, "text"));
                var scene = new Scene
                {
                    Title = FirstNonEmpty(Text(sceneNode, "title"), $"Сцена {sceneOrder + 1}"),
                    Description = ComposeSceneDescription(sceneNode),
                    Status = ParseStatus(Text(sceneNode, "status")),
                    Order = sceneOrder,
                    ChapterId = chapter.Id,
                    SourceId = NullIfEmpty(Text(sceneNode, "id")),
                    Document = document,
                    Words = WordCounting.Count(document),
                };

                scene.Metadata.Notes = NullIfEmpty(Text(sceneNode, "notes"));
                scene.Metadata.IsKey = sceneNode["isKey"]?.GetValue<bool>() ?? false;
                // Стабильный Id нужен сохранённым связям PlotLineStep.SceneIds.
                if (Guid.TryParse(Text(sceneNode, "scriselfId"), out var savedId) && savedId != Guid.Empty)
                    scene.Id = savedId;
                if (sceneNode["scriselfStepOrder"] is JsonArray stepOrder)
                {
                    foreach (var value in stepOrder)
                    {
                        if (Guid.TryParse(value?.ToString(), out var stepId))
                            scene.Metadata.PlotLineStepOrder.Add(stepId);
                    }
                }
                chapter.Scenes.Add(scene);

                foreach (var plotlineId in ReadPlotlineIds(sceneNode))
                {
                    if (!scenesByPlotline.TryGetValue(plotlineId, out var ids))
                    {
                        ids = [];
                        scenesByPlotline[plotlineId] = ids;
                    }

                    ids.Add(scene.Id);
                }

                sceneOrder++;
            }

            // Старые файлы хранили текст главы целиком в `chapterText`, а сцены
            // оставляли пустыми: разбирать в сценах было нечего, и текст пропадал
            // при открытии. Переносим его в первую сцену главы — он не должен теряться.
            if (!hasSceneText && NullIfEmpty(Text(chapterNode, "chapterText")) is { } chapterText)
            {
                var target = chapter.Scenes.FirstOrDefault();
                if (target is null)
                {
                    target = new Scene { Title = "Сцена 1", Order = 0, ChapterId = chapter.Id };
                    chapter.Scenes.Add(target);
                }

                var document = PlainTextDocument.FromText(chapterText);
                target.Document = document;
                target.Words = WordCounting.Count(document);
            }

            legacyBook.Chapters.Add(chapter);
            chapterOrder++;
        }

        project.PlotLines.AddRange(ReadLegacyPlotLines(project, content, scenesByPlotline));
        project.Characters.AddRange(ReadLegacyCharacters(project, content));
        project.Locations.AddRange(ReadLegacyLibraryEntries(content, "locations", project, CreateLocation));
        project.StoryObjects.AddRange(ReadLegacyLibraryEntries(content, "objects", project, CreateStoryObject));

        // Полные карточки и шаги линий лежат в нашем разделе документа; если его нет,
        // остаются перенесённые из старого формата данные (ТЗ п. 5, 27; ADR 010).
        ApplyScriSelfExtras(project, content);

        // Документы сцен перекрывают текст, разобранный из плоского поля `text`.
        ApplySceneDocuments(project, content);
        ApplySceneVersions(project, content);

        return project;
    }

    /// <summary>
    /// Записать проект в исходный документ: наши данные заменяют перенесённые поля,
    /// остальные остаются нетронутыми, чтобы файл читался старыми версиями приложения.
    /// </summary>
    public static void ApplyToContent(Project project, JsonObject manifest, JsonObject content, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(content);

        manifest["name"] = project.Name;
        manifest["updatedAt"] = now.ToString("O", CultureInfo.InvariantCulture);

        // Узлы клонируются: узел JSON имеет одного родителя, а мы пересобираем массивы.
        var legacyChapters = CloneObjects(content["chapters"] as JsonArray);
        var legacyBook = content["book"] as JsonObject;
        var bookTitle = project.EnumerateSeries()
            .SelectMany(series => series.Books)
            .FirstOrDefault(book => !book.IsDeleted)?.Title;

        if (legacyBook is not null && bookTitle is not null)
        {
            legacyBook["title"] = bookTitle;
        }

        var chapters = new JsonArray();
        var chapterNumber = 0;
        foreach (var chapter in project.EnumerateSeries()
                     .SelectMany(series => series.Books)
                     .SelectMany(book => book.Chapters)
                     .Where(chapter => !chapter.IsDeleted))
        {
            chapterNumber++;
            var legacyChapter = FindBySourceId(legacyChapters, chapter.SourceId)
                ?? new JsonObject { ["id"] = NewId("chapter", chapterNumber) };

            legacyChapter["title"] = chapter.Title;
            legacyChapter["number"] ??= chapterNumber;
            legacyChapter["status"] ??= LegacySceneStatusInProgress;

            var legacyScenes = CloneObjects(legacyChapter["scenes"] as JsonArray);
            legacyChapter.Remove("scenes");

            // Если в старых сценах текста не было, при открытии он был взят из
            // `chapterText` и теперь записан в сцены — в главе его не дублируем.
            if (legacyChapter.ContainsKey("chapterText")
                && !legacyScenes.Any(node => NullIfEmpty(Text(node, "text")) is not null))
            {
                legacyChapter["chapterText"] = string.Empty;
            }

            var scenes = new JsonArray();
            var sceneNumber = 0;
            foreach (var scene in chapter.Scenes.Where(scene => !scene.IsDeleted))
            {
                sceneNumber++;
                var legacyScene = FindBySourceId(legacyScenes, scene.SourceId)
                    ?? new JsonObject { ["id"] = NewId("scene", sceneNumber) };

                legacyScene["title"] = scene.Title;
                legacyScene["text"] = scene.Document.ToPlainText();
                legacyScene["status"] = ToLegacyStatus(scene.Status);
                legacyScene["scriselfId"] = scene.Id.ToString();
                legacyScene["scriselfStepOrder"] = new JsonArray(scene.Metadata.PlotLineStepOrder
                    .Select(id => (JsonNode?)JsonValue.Create(id.ToString())).ToArray());
                scenes.Add(legacyScene);

                // Сцена без старого идентификатора получает его здесь. Пока SourceId
                // пуст, при следующем открытии она получила бы новый Guid, и документ
                // со стилом не нашёлся бы по ключу в разделе `scriself`.
                scene.SourceId ??= NullIfEmpty(Text(legacyScene, "id"));
            }

            legacyChapter["scenes"] = scenes;
            legacyChapter.Remove("chapters");
            chapters.Add(legacyChapter);
        }

        content.Remove("chapters");
        content["chapters"] = chapters;

        ApplyPlotLines(project, content);
        WriteCharacters(project, content);
        WriteLibraryEntries(
            content,
            "locations",
            "loc",
            project.Locations.Where(location => !location.IsDeleted),
            location => location.SourceId,
            (location, id) => location.SourceId = id,
            (location, node) =>
            {
                node["name"] = location.Name;
                node["description"] = location.Description ?? string.Empty;
            });
        WriteLibraryEntries(
            content,
            "objects",
            "obj",
            project.StoryObjects.Where(item => !item.IsDeleted),
            item => item.SourceId,
            (item, id) => item.SourceId = id,
            (item, node) =>
            {
                node["name"] = item.Name;
                node["description"] = item.Description ?? string.Empty;
            });
        WriteScriSelfExtras(project, content);
    }

    private static void ApplyPlotLines(Project project, JsonObject content)
    {
        var timeline = content["timeline"] as JsonObject;
        if (timeline is null)
        {
            timeline = new JsonObject();
            content["timeline"] = timeline;
        }

        var legacyPlotlines = CloneObjects(timeline["plotlines"] as JsonArray);
        timeline.Remove("plotlines");

        var plotlines = new JsonArray();
        foreach (var plotline in project.PlotLines)
        {
            var legacyPlotline = FindBySourceId(legacyPlotlines, plotline.SourceId);
            if (legacyPlotline is null)
            {
                // Идентификатор запоминается в модели: иначе следующий шаг не найдёт
                // эту линию и создаст вторую копию.
                plotline.SourceId = NewId("plotline", plotlines.Count + 1);
                legacyPlotline = new JsonObject { ["id"] = plotline.SourceId };
            }

            legacyPlotline["name"] = plotline.Name;
            if (plotline.ColorHex is { Length: > 0 } color)
            {
                legacyPlotline["color"] = color;
            }

            plotlines.Add(legacyPlotline);
        }

        timeline["plotlines"] = plotlines;
    }

    /// <summary>
    /// Персонажи, перенесённые из старого формата: карточка заполняется только теми
    /// полями, которые есть в старых версиях. Остальное автор допишет у нас (ADR 010).
    /// </summary>
    private static List<Character> ReadLegacyCharacters(Project project, JsonObject content)
    {
        var characters = new List<Character>();
        var nodes = (content["library"] as JsonObject)?["characters"] as JsonArray ?? [];
        var index = 0;
        foreach (var node in nodes.OfType<JsonObject>())
        {
            characters.Add(new Character
            {
                ProjectId = project.Id,
                Name = FirstNonEmpty(Text(node, "name"), "Персонаж"),
                Description = NullIfEmpty(Text(node, "description")),
                SourceId = NullIfEmpty(Text(node, "id")),
                Order = index,
                Age = NullIfEmpty(Text(node, "age")),
                Appearance = NullIfEmpty(Text(node, "appearance")),
                Occupation = NullIfEmpty(Text(node, "occupation")),
                Temperament = NullIfEmpty(Text(node, "temperament")),
                Values = NullIfEmpty(Text(node, "values")),
                Fears = NullIfEmpty(Text(node, "mainFear")),
                Secrets = NullIfEmpty(Text(node, "secrets")),
                Childhood = NullIfEmpty(Text(node, "family")),
                Trauma = NullIfEmpty(Text(node, "trauma")),
                ExternalGoal = NullIfEmpty(Text(node, "externalGoal")),
                InternalNeed = NullIfEmpty(Text(node, "internalNeed")),
                FalseBelief = NullIfEmpty(Text(node, "falseBelief")),
                Speech = NullIfEmpty(Text(node, "speech")),
                Mannerisms = NullIfEmpty(Text(node, "quirks")),
                Notes = NullIfEmpty(Text(node, "notes")),
                ArcSummary = NullIfEmpty(Text(node, "arcSummary")),
            });
            index++;
        }

        return characters;
    }

    /// <summary>
    /// Записи старого раздела <c>library</c> (локации, объекты). Карточка заполняется
    /// только теми полями, что есть в старых версиях; остальное автор допишет у нас (ADR 010).
    /// </summary>
    private static List<T> ReadLegacyLibraryEntries<T>(
        JsonObject content,
        string key,
        Project project,
        Func<Project, JsonObject, int, T> create)
    {
        var entries = new List<T>();
        var nodes = (content["library"] as JsonObject)?[key] as JsonArray ?? [];
        var index = 0;
        foreach (var node in nodes.OfType<JsonObject>())
        {
            entries.Add(create(project, node, index));
            index++;
        }

        return entries;
    }

    private static Location CreateLocation(Project project, JsonObject node, int index) => new()
    {
        ProjectId = project.Id,
        Name = FirstNonEmpty(Text(node, "name"), "Локация"),
        Description = NullIfEmpty(FirstNonEmpty(Text(node, "description"), Text(node, "extra"))),
        Notes = NullIfEmpty(Text(node, "notes")),
        SourceId = NullIfEmpty(Text(node, "id")),
        Order = index,
    };

    private static StoryObject CreateStoryObject(Project project, JsonObject node, int index) => new()
    {
        ProjectId = project.Id,
        Name = FirstNonEmpty(Text(node, "name"), "Объект"),
        Description = NullIfEmpty(FirstNonEmpty(Text(node, "description"), Text(node, "extra"))),
        Notes = NullIfEmpty(Text(node, "notes")),
        SourceId = NullIfEmpty(Text(node, "id")),
        Order = index,
    };

    private static List<PlotLine> ReadLegacyPlotLines(
        Project project,
        JsonObject content,
        Dictionary<string, List<Guid>> scenesByPlotline)
    {
        var lines = new List<PlotLine>();
        var plotlines = (content["timeline"] as JsonObject)?["plotlines"] as JsonArray ?? [];
        foreach (var plotlineNode in plotlines.OfType<JsonObject>())
        {
            var plotline = new PlotLine
            {
                ProjectId = project.Id,
                Name = FirstNonEmpty(Text(plotlineNode, "name"), "Сюжетная линия"),
                Description = NullIfEmpty(Text(plotlineNode, "description")),
                ColorHex = NullIfEmpty(Text(plotlineNode, "color")),
                SourceId = NullIfEmpty(Text(plotlineNode, "id")),
            };

            if (plotline.SourceId is { } plotlineId && scenesByPlotline.TryGetValue(plotlineId, out var sceneIds))
            {
                plotline.SceneIds.AddRange(sceneIds);
            }

            lines.Add(plotline);
        }

        return lines;
    }

    /// <summary>
    /// Наш раздел документа: полные карточки персонажей и шаги линий. Старые версии
    /// приложения его не читают и просто переносят дальше, поэтому формат совместим.
    /// </summary>
    private static void ApplyScriSelfExtras(Project project, JsonObject content)
    {
        if (content[ExtrasKey] is not JsonObject extras)
        {
            return;
        }

        MergeLibraryList(
            extras["characters"] as JsonArray,
            CoreJsonContext.Default.ListCharacter,
            project.Characters,
            character => character.SourceId,
            character => character.Id);
        MergeLibraryList(
            extras["plotlines"] as JsonArray,
            CoreJsonContext.Default.ListPlotLine,
            project.PlotLines,
            line => line.SourceId,
            line => line.Id);
        MergeLibraryList(
            extras["locations"] as JsonArray,
            CoreJsonContext.Default.ListLocation,
            project.Locations,
            location => location.SourceId,
            location => location.Id);
        MergeLibraryList(
            extras["objects"] as JsonArray,
            CoreJsonContext.Default.ListStoryObject,
            project.StoryObjects,
            item => item.SourceId,
            item => item.Id);
    }

    /// <summary>
    /// Дополняет список сущностей данными из нашего раздела документа. Полные карточки
    /// из раздела идут первыми, а перенесённые из старого формата добавляются, если их
    /// там не было (ADR 010).
    /// </summary>
    private static void MergeLibraryList<T>(
        JsonArray? node,
        JsonTypeInfo<List<T>> typeInfo,
        List<T> target,
        Func<T, string?> sourceId,
        Func<T, Guid> id)
    {
        if (ReadList(node, typeInfo) is not List<T> restored)
        {
            return;
        }

        var knownSourceIds = restored
            .Select(sourceId)
            .Where(value => !string.IsNullOrEmpty(value))
            .ToHashSet(StringComparer.Ordinal);
        var knownIds = restored.Select(id).ToHashSet();
        var merged = new List<T>(restored);
        merged.AddRange(target.Where(item =>
            (sourceId(item) is null || !knownSourceIds.Contains(sourceId(item)))
            && !knownIds.Contains(id(item))));

        target.Clear();
        target.AddRange(merged);
    }

    /// <summary>
    /// Зеркалит имя персонажа и описание в старый раздел документа: даже версия без
    /// нашего раздела покажет правильное имя (ТЗ п. 5).
    /// </summary>
    private static void WriteCharacters(Project project, JsonObject content)
    {
        var library = content["library"] as JsonObject ?? new JsonObject();
        content["library"] = library;

        var legacyCharacters = CloneObjects(library["characters"] as JsonArray);
        library.Remove("characters");

        var characters = new JsonArray();
        var number = 0;
        foreach (var character in project.Characters.Where(character => !character.IsDeleted))
        {
            number++;
            var legacyCharacter = FindBySourceId(legacyCharacters, character.SourceId);
            if (legacyCharacter is null)
            {
                character.SourceId = NewId("char", number);
                legacyCharacter = new JsonObject { ["id"] = character.SourceId };
            }

            legacyCharacter["name"] = character.Name;
            legacyCharacter["description"] = character.Description ?? string.Empty;
            characters.Add(legacyCharacter);
        }

        library["characters"] = characters;
    }

    /// <summary>
    /// Зеркалит имя и описание в старый раздел <c>library</c>: даже версия без нашего
    /// раздела покажет правильные данные. Прочие поля исходных записей сохраняются,
    /// чтобы файл читался старыми версиями приложения (ТЗ п. 5).
    /// </summary>
    private static void WriteLibraryEntries<T>(
        JsonObject content,
        string key,
        string idPrefix,
        IEnumerable<T> items,
        Func<T, string?> sourceId,
        Action<T, string> setSourceId,
        Action<T, JsonObject> apply)
    {
        var library = content["library"] as JsonObject ?? new JsonObject();
        content["library"] = library;

        var legacyEntries = CloneObjects(library[key] as JsonArray);
        library.Remove(key);

        var entries = new JsonArray();
        var number = 0;
        foreach (var item in items)
        {
            number++;
            var legacyEntry = FindBySourceId(legacyEntries, sourceId(item));
            if (legacyEntry is null)
            {
                var newId = NewId(idPrefix, number);
                setSourceId(item, newId);
                legacyEntry = new JsonObject { ["id"] = newId };
            }

            apply(item, legacyEntry);
            entries.Add(legacyEntry);
        }

        library[key] = entries;
    }

    /// <summary>Сцены в том же порядке, в каком они пишутся в <c>content.json</c>.</summary>
    private static IEnumerable<Scene> OrderedScenes(Project project) =>
        project.EnumerateSeries()
            .SelectMany(series => series.Books)
            .SelectMany(book => book.Chapters)
            .SelectMany(chapter => chapter.Scenes)
            .Where(scene => !scene.IsDeleted);

    /// <summary>
    /// Полные данные, которых нет в старом формате: карточки и шаги линий.
    /// Здесь же хранится канонический документ сцены: старый <c>text</c> — плоская
    /// строка, и оформление фрагментов в нём не помещается, поэтому без этого раздела
    /// сохранение схлопывало стиль до простого текста (ADR 003, шаг 3.1.5).
    /// </summary>
    private static void WriteScriSelfExtras(Project project, JsonObject content)
    {
        JsonNode? ToNode<T>(List<T> items, JsonTypeInfo<List<T>> typeInfo) =>
            JsonNode.Parse(JsonSerializer.Serialize(items, typeInfo));

        content[ExtrasKey] = new JsonObject
        {
            ["version"] = ExtrasVersion,
            ["characters"] = ToNode(
                [.. project.Characters.Where(character => !character.IsDeleted)],
                CoreJsonContext.Default.ListCharacter),
            ["plotlines"] = ToNode(
                [.. project.PlotLines.Where(line => !line.IsDeleted)],
                CoreJsonContext.Default.ListPlotLine),
            ["locations"] = ToNode(
                [.. project.Locations.Where(location => !location.IsDeleted)],
                CoreJsonContext.Default.ListLocation),
            ["objects"] = ToNode(
                [.. project.StoryObjects.Where(item => !item.IsDeleted)],
                CoreJsonContext.Default.ListStoryObject),
            ["documents"] = WriteSceneDocuments(project),
            ["sceneVersions"] = WriteSceneVersions(project),
        };
    }

    /// <summary>
    /// Ключ документа сцены в разделе <c>scriself</c>. У старых сцен он уже есть
    /// в <c>id</c>, а наш <c>Guid</c> при каждом открытии новый, поэтому ключом
    /// служит старый идентификатор: он переживает переоткрытие файла.
    /// </summary>
    private static string DocumentKey(Scene scene) => scene.SourceId ?? scene.Id.ToString();

    private static JsonObject WriteSceneVersions(Project project)
    {
        var versions = new JsonObject();
        foreach (var scene in OrderedScenes(project).Where(scene => scene.Versions.Count > 0))
            versions[DocumentKey(scene)] = JsonSerializer.SerializeToNode(scene.Versions, CoreJsonContext.Default.ListSceneVersion);
        return versions;
    }

    private static void ApplySceneVersions(Project project, JsonObject content)
    {
        var node = content[ExtrasKey]?["sceneVersions"];
        if (node is null) return;
        if (node is not JsonObject versions) throw new System.IO.InvalidDataException("Повреждён раздел истории сцен.");
        foreach (var scene in OrderedScenes(project))
        {
            if (versions[DocumentKey(scene)] is not { } saved) continue;
            var items = JsonSerializer.Deserialize(saved.ToJsonString(), CoreJsonContext.Default.ListSceneVersion);
            if (items is null || items.Any(v => v is null || v.Document is null || v.Id == Guid.Empty || string.IsNullOrWhiteSpace(v.Name))
                || items.Select(v => v.Id).Distinct().Count() != items.Count)
                throw new System.IO.InvalidDataException("Повреждена версия сцены.");
            scene.Versions.AddRange(items);
            foreach (var version in items) ReviewEditing.Validate(version.Document);
        }
    }

    /// <summary>
    /// Документы сцен. Ключ не привязан к порядку, поэтому раздел не расходится
    /// с порядком сцен в <c>content.json</c>.
    /// </summary>
    private static JsonObject WriteSceneDocuments(Project project)
    {
        var documents = new JsonObject();
        foreach (var scene in OrderedScenes(project))
        {
            documents[DocumentKey(scene)] = JsonNode.Parse(
                JsonSerializer.Serialize(scene.Document, CoreJsonContext.Default.RichDocument));
        }

        return documents;
    }

    /// <summary>
    /// Возвращает сохранённые документы сцен. Старые файлы раздела не имеют, и тогда
    /// текст остаётся разобранным из плоского поля <c>text</c>.
    /// </summary>
    private static void ApplySceneDocuments(Project project, JsonObject content)
    {
        if (content[ExtrasKey] is not JsonObject extras
            || extras["documents"] is not JsonObject documents)
        {
            return;
        }

        foreach (var scene in OrderedScenes(project))
        {
            if (documents[DocumentKey(scene)] is not JsonNode node)
            {
                continue;
            }

            RichDocument? document;
            try
            {
                document = JsonSerializer.Deserialize(node.ToJsonString(), CoreJsonContext.Default.RichDocument);
            }
            catch (JsonException) when (node is not JsonObject objectNode || objectNode["review"] is null)
            {
                // Повреждённый документ не должен отменять открытие проекта: текст
                // из плоского поля уже загружен, теряется только оформление.
                continue;
            }

            if (document is null)
            {
                continue;
            }

            ReviewEditing.Validate(document);
            if (document.IsEmpty() && !document.Review.TrackChanges && document.Review.Comments.Count == 0 && document.Review.Changes.Count == 0)
                continue;
            scene.Document = document;
            scene.Words = WordCounting.Count(document);
        }
    }

    private static List<T>? ReadList<T>(JsonArray? array, JsonTypeInfo<List<T>> typeInfo) =>
        array is null ? null : JsonSerializer.Deserialize(array.ToJsonString(), typeInfo);

    private static JsonObject? FindBySourceId(IEnumerable<JsonObject> nodes, string? sourceId) =>
        sourceId is null
            ? null
            : nodes.FirstOrDefault(node => string.Equals(Text(node, "id"), sourceId, StringComparison.Ordinal));

    private static List<JsonObject> CloneObjects(JsonArray? array) =>
        array is null
            ? []
            : array.OfType<JsonObject>().Select(node => (JsonObject)node.DeepClone()).ToList();

    private static string NewId(string prefix, int number) =>
        string.Create(CultureInfo.InvariantCulture, $"{prefix}_{number:000}");

    private static IEnumerable<string> ReadPlotlineIds(JsonObject scene)
    {
        var ids = scene["plotlineIds"] as JsonArray;
        if (ids is not null)
        {
            foreach (var id in ids)
            {
                if (NullIfEmpty(id?.GetValue<string>()) is { } value)
                {
                    yield return value;
                }
            }
        }

        if (NullIfEmpty(Text(scene, "plotlineId")) is { } single)
        {
            yield return single;
        }
    }

    private static string? ComposeSceneDescription(JsonObject scene)
    {
        var parts = new List<string>();
        Append(parts, "Цель", Text(scene, "goal"));
        Append(parts, "Конфликт", Text(scene, "conflict"));
        Append(parts, "Результат", Text(scene, "result"));
        return parts.Count == 0 ? null : string.Join('\n', parts);
    }

    private static void Append(List<string> parts, string caption, string? value)
    {
        if (NullIfEmpty(value) is { } text)
        {
            parts.Add($"{caption}: {text}");
        }
    }

    private static SceneStatus ParseStatus(string? status) => status?.ToLowerInvariant() switch
    {
        LegacySceneStatusIdea or "planned" => SceneStatus.Idea,
        LegacySceneStatusInProgress or "revised" or "editing" => SceneStatus.Revised,
        LegacySceneStatusFinal or "done" or "complete" or "completed" => SceneStatus.Final,
        _ => SceneStatus.Draft,
    };

    private static string ToLegacyStatus(SceneStatus status) => status switch
    {
        SceneStatus.Idea => LegacySceneStatusIdea,
        SceneStatus.Revised => LegacySceneStatusInProgress,
        SceneStatus.Final => LegacySceneStatusFinal,
        _ => LegacySceneStatusDraft,
    };

    private static DateTimeOffset ParseTime(string? value, DateTimeOffset fallback) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : fallback;

    private static string? Text(JsonObject? node, string property) =>
        node?[property] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
}
