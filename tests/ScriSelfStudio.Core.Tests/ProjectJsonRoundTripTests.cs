namespace ScriSelfStudio.Core.Tests;

using System.Text.Json;
using ScriSelfStudio.Core.Json;
using ScriSelfStudio.Core.Model;
using ScriSelfStudio.Core.Text;
using ScriSelfStudio.TestSupport;

/// <summary>
/// Совместимость с документами, сохранёнными до появления составного
/// оформления (ADR 003, шаг 3.1.5). Старый файл хранил только <c>format</c>;
/// такие файлы должны открываться без потерь и сохраняться в новом виде.
/// </summary>
public sealed class SpanStyleCompatibilityTests
{
    [Fact]
    public void Document_with_legacy_format_field_is_read()
    {
        const string legacy = """
        {
          "schemaVersion": 1,
          "blocks": [
            {
              "id": "11111111-1111-1111-1111-111111111111",
              "kind": "Paragraph",
              "alignment": "Left",
              "indentLevel": 0,
              "spans": [
                { "text": "обычный ", "format": 0 },
                { "text": "жирный", "format": 1 }
              ]
            }
          ]
        }
        """;

        var document = JsonSerializer.Deserialize(legacy, CoreJsonContext.Default.RichDocument);

        Assert.NotNull(document);
        var spans = document.Blocks[0].Spans;
        Assert.Equal(2, spans.Count);
        Assert.Equal(TextFormat.None, spans[0].Style.Flags);
        Assert.Equal(TextFormat.Bold, spans[1].Style.Flags);

        // Шрифт и цвета не заданы — наследуются от темы.
        Assert.Null(spans[1].Style.FontFamily);
        Assert.False(spans[1].Style.Foreground.IsSet);
    }

    [Fact]
    public void Document_saved_with_style_keeps_flags_font_and_colors()
    {
        var document = RichDocument.CreateEmpty();
        document.Blocks[0].Spans.Add(new TextSpan
        {
            Text = "реплика",
            Style = new SpanStyle
            {
                Flags = TextFormat.Bold | TextFormat.Italic,
                FontFamily = "Cascadia Mono",
                FontSize = 14d,
                Foreground = TextColor.Parse("#3366FF"),
                Highlight = TextColor.Parse("#FFFF00"),
            },
        });

        var json = JsonSerializer.Serialize(document, CoreJsonContext.Default.RichDocument);
        var restored = JsonSerializer.Deserialize(json, CoreJsonContext.Default.RichDocument);

        Assert.NotNull(restored);
        Assert.NotEmpty(restored.Blocks);
        Assert.NotEmpty(restored.Blocks[0].Spans);
        var style = restored.Blocks[0].Spans[0].Style;
        Assert.Equal(TextFormat.Bold | TextFormat.Italic, style.Flags);
        Assert.Equal("Cascadia Mono", style.FontFamily);
        Assert.Equal(14d, style.FontSize);
        Assert.Equal(TextColor.Parse("#3366FF"), style.Foreground);
        Assert.Equal(TextColor.Parse("#FFFF00"), style.Highlight);

        // Устаревшее поле не пишется: иначе появился бы второй источник правды.
        Assert.DoesNotContain("format", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Color_parse_accepts_both_hex_forms_and_rejects_garbage()
    {
        Assert.Equal(new TextColor(0x33, 0x66, 0xFF), TextColor.Parse("#3366FF"));
        Assert.Equal(new TextColor(0x33, 0x66, 0xFF), TextColor.Parse("3366ff"));
        Assert.Equal(new TextColor(0x33, 0x66, 0xFF, 0x80), TextColor.Parse("#803366FF"));
        Assert.False(TextColor.Parse("#12345").IsSet);
        Assert.False(TextColor.Parse("не цвет").IsSet);
        Assert.False(TextColor.Parse(null).IsSet);
    }

    [Fact]
    public void Intersect_marks_mixed_font_and_color_as_unset()
    {
        var left = new SpanStyle
        {
            Flags = TextFormat.Bold,
            FontFamily = "Inter",
            Foreground = TextColor.Parse("#FF0000"),
        };
        var right = new SpanStyle
        {
            Flags = TextFormat.Bold | TextFormat.Italic,
            FontFamily = "Consolas",
            Foreground = TextColor.Parse("#00FF00"),
        };

        var mixed = left.Intersect(right);

        Assert.Equal(TextFormat.Bold, mixed.Flags);
        Assert.Null(mixed.FontFamily);
        Assert.False(mixed.Foreground.IsSet);

        // Одинаковое оформление пересекается с собой без потерь.
        var same = left.Intersect(left);
        Assert.Equal("Inter", same.FontFamily);
        Assert.Equal(TextColor.Parse("#FF0000"), same.Foreground);
    }
}

public sealed class ProjectJsonRoundTripTests
{
    [Fact]
    public void Project_survives_serialization_with_cyrillic_emoji_and_formatting()
    {
        var project = SampleProjects.CreateBookWithScenes();
        var scene = project.EnumerateScenes().First();
        scene.Revision = 7;
        scene.Status = SceneStatus.Revised;
        scene.Metadata.Tags.Add("ёжик");
        scene.Metadata.Tags.Add("🚀");
        project.PlotLines.Add(new PlotLine
        {
            ProjectId = project.Id,
            Name = "Основная линия",
            SceneIds = { scene.Id },
        });

        var json = JsonSerializer.Serialize(project, CoreJsonContext.Default.Project);
        var restored = JsonSerializer.Deserialize(json, CoreJsonContext.Default.Project);

        Assert.NotNull(restored);
        Assert.Equal(project.Id, restored.Id);
        Assert.Equal("Роман", restored.Name);
        Assert.Equal(project.Series.Count, restored.Series.Count);

        var restoredScenes = restored.EnumerateScenes().ToList();
        Assert.Equal(project.EnumerateScenes().Count(), restoredScenes.Count);
        var restoredScene = restoredScenes[0];
        Assert.Equal(scene.Id, restoredScene.Id);
        Assert.Equal(7, restoredScene.Revision);
        Assert.Equal(SceneStatus.Revised, restoredScene.Status);
        Assert.Equal(SampleProjects.SceneText, restoredScene.Document.ToPlainText());
        Assert.Equal(TextFormat.Bold, restoredScene.Document.Blocks[0].Spans[0].Style.Flags);
        Assert.Contains("ёжик", restoredScene.Metadata.Tags);
        Assert.Contains("🚀", restoredScene.Metadata.Tags);

        var plotLine = Assert.Single(restored.PlotLines);
        Assert.Equal("Основная линия", plotLine.Name);
        Assert.Equal([scene.Id], plotLine.SceneIds);
    }

    [Fact]
    public void Missing_optional_values_do_not_break_deserialization()
    {
        var project = SampleProjects.CreateBookWithScenes();
        project.Description = null;

        var json = JsonSerializer.Serialize(project, CoreJsonContext.Default.Project);
        var restored = JsonSerializer.Deserialize(json, CoreJsonContext.Default.Project);

        Assert.NotNull(restored);
        Assert.Null(restored.Description);
    }
}

/// <summary>
/// Совместимость ключей данных: у авторов уже есть проекты, где коллекции в JSON
/// назывались <c>arcSteps</c> (этапы арки персонажа) и <c>stages</c> (шаги линии).
/// Переименование «шаг линии / этап арки» не должно терять эти данные при чтении.
/// </summary>
public sealed class StoryTerminologyCompatibilityTests
{
    [Fact]
    public void Character_arc_stages_serialize_under_legacy_key()
    {
        var project = ProjectFactory.CreateProject("Роман");
        var character = ProjectFactory.CreateCharacter(project, "Анна");

        var json = JsonSerializer.Serialize(character, CoreJsonContext.Default.Character);

        Assert.Contains("\"arcSteps\"", json);
        Assert.DoesNotContain("\"arcStages\"", json);
    }

    [Fact]
    public void Character_arc_stages_are_read_from_legacy_key()
    {
        const string legacy = """
        {
          "id": "22222222-2222-2222-2222-222222222222",
          "projectId": "33333333-3333-3333-3333-333333333333",
          "name": "Анна",
          "arcSteps": [
            {
              "id": "44444444-4444-4444-4444-444444444444",
              "title": "Триггер",
              "kind": "Trigger"
            }
          ]
        }
        """;

        var character = JsonSerializer.Deserialize(legacy, CoreJsonContext.Default.Character);

        Assert.NotNull(character);
        var stage = Assert.Single(character.ArcStages);
        Assert.Equal("Триггер", stage.Title);
        Assert.Equal(CharacterArcStageKind.Trigger, stage.Kind);
    }

    [Fact]
    public void Plot_line_participant_serializes_character_stage_under_legacy_key()
    {
        var participant = new StepParticipant
        {
            CharacterId = Guid.NewGuid(),
            CharacterArcStageId = Guid.NewGuid(),
        };

        var json = JsonSerializer.Serialize(participant, CoreJsonContext.Default.StepParticipant);

        Assert.Contains("\"characterArcStepId\"", json);
        Assert.DoesNotContain("\"characterArcStageId\"", json);

        var restored = JsonSerializer.Deserialize(json, CoreJsonContext.Default.StepParticipant);
        Assert.NotNull(restored);
        Assert.Equal(participant.CharacterArcStageId, restored.CharacterArcStageId);
    }
}