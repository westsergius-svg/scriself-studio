namespace ScriSelfStudio.UiSmokeTests;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using ScriSelfStudio.App.Shell;
using ScriSelfStudio.App.Views;
using ScriSelfStudio.ArchitectureUI;
using ScriSelfStudio.Contracts.Books;
using ScriSelfStudio.Contracts.Dtos;
using ScriSelfStudio.Contracts.Errors;
using ScriSelfStudio.Contracts.Projects;
using ScriSelfStudio.Contracts.Settings;
using ScriSelfStudio.Core.Model;
using ScriSelfStudio.EditorUI;
using ScriSelfStudio.Launcher;
using ScriSelfStudio.Presentation.Theme;
using ScriSelfStudio.Presentation.Export;
using ScriSelfStudio.StoryUI;

public sealed class StudioDesignTests
{
    [AvaloniaFact]
    public async Task Studio_workspace_renders_real_content_and_tools_navigate_all_screens()
    {
        using var context = await SceneContext.CreateAsync();
        var provider = context.Provider;
        var projects = provider.GetRequiredService<IProjectService>();
        var books = provider.GetRequiredService<IBookArchitectureService>();
        var shell = provider.GetRequiredService<ShellViewModel>();
        await shell.SelectThemeCommand.ExecuteAsync(shell.Themes.Single(theme => theme.Value == AppTheme.Studio));
        projects.Mutate(project =>
        {
            project.Name = "Дом у реки";
            var series = project.Series[0];
            series.Title = "Дом у реки";
            series.Books[0].Title = "Возвращение";
            series.Books[0].Chapters[0].Title = "Письмо";
            series.Books[0].Chapters[0].Scenes[0].Title = "Свет в окне";
            return Result.Success;
        });
        var book = books.GetBooks()[0];
        await books.CreateSceneAsync(new() { ChapterId = book.Chapters[0].Id, Title = "За закрытой дверью" });
        await books.CreateSceneAsync(new() { ChapterId = book.Chapters[0].Id, Title = "Разговор на мосту" });
        await books.CreateChapterAsync(new() { BookId = book.Id, Title = "Обратная сторона" });
        await books.CreateSceneAsync(new() { ChapterId = books.GetBooks()[0].Chapters[1].Id, Title = "Незнакомый почерк" });
        await books.SaveCharacterAsync(new CharacterDto
        {
            Name = "Анна", Role = CharacterRole.Protagonist, Description = "После долгого отсутствия возвращается в родной город.",
            Appearance = "Серые глаза, короткие тёмные волосы.", Temperament = "Наблюдательная, сдержанная. Не любит оставлять вопросы без ответа.",
            ExternalGoal = "Найти автора письма", SpeechHabits = "Сначала разберёмся.",
            ArcStages = [new() { Title = "Первый шаг", Description = "Решает открыть письмо и узнать правду о прошлом семьи." }],
        });
        await books.SaveCharacterAsync(new CharacterDto { Name = "Михаил", Description = "Хранитель старого дома у реки.", Appearance = "Седые виски, привычка поправлять очки." });
        var anna = books.GetCharacters()[0];
        await books.SavePlotLineAsync(new PlotLineDto { Name = "Письмо из прошлого", Description = "Следы семейной истории приводят Анну к старому дому.",
            Steps = [new() { Title = "Находка", Description = "В ящике стола Анна обнаруживает письмо без подписи.",
                Participants = [new() { CharacterId = anna.Id, CharacterArcStageId = anna.ArcStages[0].Id }] }] });
        await books.SavePlotLineAsync(new PlotLineDto { Name = "Возвращение домой", Description = "Примирение с городом, который Анна когда-то оставила.",
            Steps = [new() { Title = "Встреча", Description = "Михаил впускает Анну в дом и оставляет её наедине с воспоминаниями." }] });
        await projects.UpdateSceneAsync(new() { SceneId = context.SceneId, Description = "Анна возвращается в старый дом. На столе её ждёт письмо, которое меняет смысл этой поездки.",
            PlotLineStepIds = [.. books.GetPlotLines().Select(line => line.Steps[0].Id)] });
        var editor = provider.GetRequiredService<EditorViewModel>();
        editor.EditorFontFamily = "Georgia";
        editor.EditorFontSize = 18;
        editor.Text = "К утру дождь закончился. Город ещё спал, и только в окне старого дома горел свет. Анна остановилась у калитки, прислушиваясь к тишине. За этой дверью начиналось всё, что она столько лет откладывала.\n\n"
            + "На столе лежало письмо. Незнакомый почерк заполнял страницу неторопливо, оставляя широкие поля. Между строк попадались названия деревень и короткие замечания о ветре. Это было похоже на рассказ человека, который никуда не спешил.\n\n"
            + "Михаил нашёл её у окна. Он не стал спрашивать, почему она приехала так рано, а просто поставил рядом две чашки. От горячего чая поднимался пар. На мгновение Анне показалось, что в комнате стало светлее.\n\n"
            + "— Начнём с самого простого, — сказал он. — Расскажи, что ты помнишь.\n\n"
            + "Она посмотрела на первую строку. Вчера эти слова казались чужими, но теперь в них проступал знакомый ритм. Где-то за городом ждала дорога, которую однажды уже прошёл её отец.";
        var panel = provider.GetRequiredService<SceneContextViewModel>();
        panel.Refresh();
        panel.Characters[0].IsDescriptionExpanded = true;
        shell.IsScenePanelRequested = true;
        var window = provider.GetRequiredService<MainWindow>();
        window.Width = 1440;
        window.Height = 900;
        window.Show();
        try
        {
            Capture(window, "studio-editor");
            Assert.True(shell.ShowStudioChrome);
            Assert.False(shell.ShowClassicHeader || shell.ShowCabinetChrome);
            Assert.True(shell.ShowScenePanel);
            Assert.Contains("active", Find(window, "StudioEditorButton").Classes);
            var text = editor.Engine.PlainText;
            window.Width = 940;
            window.Height = 700;
            Capture(window, "studio-editor-compact");
            Assert.Equal(text, editor.Engine.PlainText);
            Assert.False(shell.ShowBookTree);
            window.Width = 1440;
            window.Height = 900;
            await editor.SaveCommand.ExecuteAsync(null);
            var directory = Environment.GetEnvironmentVariable("SCRISELF_RENDER_DIR");
            if (!string.IsNullOrWhiteSpace(directory)) Assert.True((await projects.SaveAsAsync(Path.Combine(directory, "studio-demo.scri"))).IsSuccess);

            Click(window, "StudioMapButton");
            var map = provider.GetRequiredService<ArchitectureViewModel>();
            map.Selected = map.Roots[0].Children[0].Children[0].Children[0];
            Capture(window, "studio-map");
            Assert.True(shell.IsMapActive);
            Click(window, "StudioCharactersButton");
            var characters = provider.GetRequiredService<CharactersViewModel>();
            characters.Selected = characters.Characters[0];
            Capture(window, "studio-characters");
            Assert.True(shell.IsCharactersActive);
            Click(window, "StudioLinesButton");
            var timeline = provider.GetRequiredService<TimelineViewModel>();
            timeline.SelectedLine = timeline.PlotLines[0];
            Capture(window, "studio-lines");
            Assert.True(shell.IsLinesActive);
            Click(window, "StudioSettingsButton");
            var settings = provider.GetRequiredService<SettingsViewModel>();
            settings.SelectedSection = settings.Sections.Single(section => section.Section == SettingsSection.Appearance);
            Capture(window, "studio-settings");
            Assert.True(shell.IsSettingsActive);
            var export = provider.GetRequiredService<ExportViewModel>();
            export.SelectedFormat = export.Formats.Single(format => format.Key == "DOCX");
            export.IncludeTitlePage = false;
            export.TableOfContents = false;
            Click(window, "StudioExportButton");
            Capture(window, "studio-export");
            Assert.True(shell.IsExportActive);
            Click(window, "StudioProjectsButton");
            Capture(window, "studio-projects");
            Assert.True(shell.IsLauncherActive);
            Click(window, "StudioEditorButton");
            Assert.Equal(text, editor.Engine.PlainText);
            editor.IsFullCanvas = true;
            Flush();
            Assert.False(shell.ShowStudioChrome);
            editor.IsFullCanvas = false;
            Flush();
            Assert.True(shell.ShowStudioChrome);
        }
        finally { window.Close(); }
    }

    private static Button Find(Window window, string name) => window.GetVisualDescendants().OfType<Button>().Single(button => button.Name == name);

    private static void Click(Window window, string name)
    {
        var button = Find(window, name);
        Assert.True(button.IsEffectivelyVisible && button.IsEffectivelyEnabled);
        var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        window.MouseMove(new Point(550, 20));
        Flush();
    }

    private static void Flush()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    private static void Capture(Window window, string name)
    {
        Flush();
        var directory = Environment.GetEnvironmentVariable("SCRISELF_RENDER_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        using var bitmap = window.CaptureRenderedFrame();
        Assert.NotNull(bitmap);
        bitmap.Save(Path.Combine(directory, name + ".png"), PngBitmapEncoderOptions.Default);
    }
}
