namespace ScriSelfStudio.TestSupport;

using ScriSelfStudio.Core;
using ScriSelfStudio.Core.Model;
using ScriSelfStudio.Core.Text;

/// <summary>Искусственные проекты для тестов (ТЗ п. 59).</summary>
public static class SampleProjects
{
    public const string SceneText = "Первая строка рукописи.\nВторая строка с «ёжиком» и emoji 🚀."; 

    public static Project CreateBookWithScenes(DateTimeOffset? now = null)
    {
        var timestamp = now ?? new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var project = ProjectFactory.CreateProject("Роман", "Черновик", timestamp);
        var series = ProjectFactory.CreateSeries(project, "Основная серия", timestamp);
        project.Series.Add(series);

        var book = ProjectFactory.CreateBook(project, series, "Книга первая", timestamp);
        series.Books.Add(book);

        var chapter = ProjectFactory.CreateChapter(book, "Глава 1", "Начало", timestamp);
        book.Chapters.Add(chapter);

        var scene = ProjectFactory.CreateScene(chapter, "Сцена 1", timestamp);
        scene.Document = DocumentWithText(SceneText, TextFormat.Bold);
        chapter.Scenes.Add(scene);

        var second = ProjectFactory.CreateScene(chapter, "Сцена 2", timestamp);
        chapter.Scenes.Add(second);

        return project;
    }

    public static RichDocument DocumentWithText(string text, TextFormat format = TextFormat.None)
    {
        var document = new RichDocument();
        var lines = text.Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            var block = new TextBlock { Kind = BlockKind.Paragraph };
            if (lines[index].Length > 0)
            {
                block.Spans.Add(new TextSpan
                {
                    Text = lines[index],
                    Style = SpanStyle.Default.WithFlags(index == 0 ? format : TextFormat.None),
                });
            }

            document.Blocks.Add(block);
        }

        return document;
    }
}
