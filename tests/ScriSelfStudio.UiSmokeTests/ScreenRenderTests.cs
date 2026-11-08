namespace ScriSelfStudio.UiSmokeTests;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using AvaloniaRichEditor.Documents;
using ScriSelfStudio.App.Views;
using ScriSelfStudio.ArchitectureUI;
using ScriSelfStudio.Contracts.Books;
using ScriSelfStudio.Contracts.Dtos;
using ScriSelfStudio.Contracts.Projects;
using ScriSelfStudio.Contracts.Settings;
using ScriSelfStudio.Core.Model;
using ScriSelfStudio.EditorUI;
using ScriSelfStudio.Launcher;
using ScriSelfStudio.StoryUI;
using ScriSelfStudio.TestSupport;
using AvaloniaRichEditor.Controls;

/// <summary>
/// Проверка, что экраны действительно рисуются: headless-платформа с Skia отдаёт кадр.
/// Кадры сохраняются в каталог из переменной окружения <c>SCRISELF_RENDER_DIR</c>,
/// если она задана, — для ручного просмотра раскладки (ТЗ п. 58).
/// </summary>
public sealed class ScreenRenderTests
{
    [AvaloniaTheory]
    [InlineData(AppTheme.Starter)]
    [InlineData(AppTheme.GreenStart)]
    [InlineData(AppTheme.Cabinet)]
    [InlineData(AppTheme.Jam)]
    public async Task All_screens_render_a_frame(AppTheme theme)
    {
        var cabinet = theme is AppTheme.Cabinet or AppTheme.Jam;
        using var workspace = new TempWorkspace();
        var path = workspace.FilePath("Источник магии.scri");
        var dialogs = new StubFileDialogService { CreatePath = path, PromptResult = "Элемент" };
        using var provider = ServiceProviderFactory.Create(workspace.Root, dialogs);
        var window = provider.GetRequiredService<MainWindow>();
        var launcher = provider.GetRequiredService<LauncherViewModel>();
        await launcher.InitializeAsync();
        var themeShell = provider.GetRequiredService<ScriSelfStudio.App.Shell.ShellViewModel>();
        await themeShell.SelectThemeCommand.ExecuteAsync(themeShell.Themes.Single(option => option.Value == theme));
        window.Width = 1440;
        window.Height = 768;

        // Кадр лаунчера сверяется с макетом «Мои проекты»: список наполняем
        // реальными проектами (файлы и структура настоящие), чтобы на кадре были
        // видны карточки со сводкой, выделение и панель «Контекст проекта».
        await SeedShowcaseProjectsAsync(provider, workspace);
        await launcher.CreateProjectCommand.ExecuteAsync(null);
        await BuildStructureAsync(provider, chapters: 3, scenes: 6);

        // Карточка открытого проекта обязана показывать живую сводку структуры:
        // книга, главы и сцены добавляются в проект уже после его открытия.
        Assert.Equal(1, launcher.SelectedProject?.BookCount);
        Assert.Equal(3, launcher.SelectedProject?.ChapterCount);
        Assert.Equal(6, launcher.SelectedProject?.SceneCount);

        window.Show();
        provider.GetRequiredService<ScriSelfStudio.App.Shell.ShellViewModel>().OpenLauncherCommand.Execute(null);
        Render(window, "01-launcher");

        launcher.OpenArchitectureCommand.Execute(null);
        var architecture = provider.GetRequiredService<ArchitectureViewModel>();
        architecture.Selected = architecture.Roots[0];
        await architecture.CreateBookCommand.ExecuteAsync(null);
        architecture.Selected = architecture.Roots[0].Children[0];
        await architecture.CreateChapterCommand.ExecuteAsync(null);
        architecture.Selected = architecture.Roots[0].Children[0].Children[0];
        await architecture.CreateSceneCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();
        Render(window, "02-architecture");

        architecture.Selected = architecture.Roots[0].Children[0].Children[0].Children[0];
        await architecture.OpenSceneCommand.ExecuteAsync(null);
        // Наполняем правую панель контекста сцены: персонаж и линия с шагом,
        // привязанным к открытой сцене, плюс добавление персонажа прямо из панели.
        var architectureService = provider.GetRequiredService<IBookArchitectureService>();
        var withIdan = (await architectureService.SaveCharacterAsync(new CharacterDto
        {
            Name = "Идан",
            Role = CharacterRole.Protagonist,
            ExternalGoal = "Найти источник магии",
        })).Require();
        var idanCard = withIdan.Characters.Single(candidate => candidate.Name == "Идан");
        withIdan = (await architectureService.SaveCharacterAsync(idanCard with
        {
            ArcStages =
            [
                idanCard.ArcStages[0] with
                {
                    Title = "Сопротивление",
                    Description = "Идан отказывается покинуть родной город.",
                },
            ],
        })).Require();
        var idanId = withIdan.Characters.Single(candidate => candidate.Name == "Идан").Id;
        var idanArcStageId = withIdan.Characters.Single(candidate => candidate.Name == "Идан").ArcStages[0].Id;
        await architectureService.SaveCharacterAsync(new CharacterDto
        {
            Name = "Лея",
            Role = CharacterRole.Ally,
            Description = "Поддерживает Идана.",
        });
        await architectureService.SavePlotLineAsync(new PlotLineDto
        {
            Name = "Главная линия",
            Description = "Описание главной сюжетной линии.",
            ColorHex = "#4A7DFF",
            Status = PlotLineStatus.InProgress,
            Steps =
            [
                new PlotLineStepDto
                {
                    Title = "Поиск",
                    Description = "Идан начинает поиски источника магии.",
                    Participants =
                    [
                        new StepParticipantDto
                        {
                            CharacterId = idanId,
                            CharacterArcStageId = idanArcStageId,
                        },
                    ],
                },
                new PlotLineStepDto
                {
                    Title = "Развязка",
                    Description = "Идан находит источник магии.",
                },
            ],
        });

        var contextPanel = provider.GetRequiredService<ScriSelfStudio.App.Shell.SceneContextViewModel>();
        contextPanel.Refresh();
        contextPanel.SelectedStepOption = contextPanel.StepOptions.First();
        await contextPanel.AddStepCommand.ExecuteAsync(null);
        contextPanel.SelectedCharacterOption = contextPanel.CharacterOptions.First(option => option.Name == "Лея");
        await contextPanel.AddCharacterCommand.ExecuteAsync(null);
        Assert.False(contextPanel.HasError, contextPanel.ErrorMessage);
        Assert.Single(contextPanel.AssignedSteps);
        Assert.Equal(2, contextPanel.Characters.Count);
        var assignedFirstStep = contextPanel.AssignedSteps[0];
        assignedFirstStep.ToggleDescriptionCommand.Execute(null);
        Assert.True(assignedFirstStep.IsDescriptionExpanded);
        Assert.Equal("Описание главной сюжетной линии.", assignedFirstStep.LineDescription);
        Assert.Equal("Идан начинает поиски источника магии.", assignedFirstStep.StepDescription);
        // Раскрытый этап обязан называть себя и показывать своё описание, а не только описание линии.
        Assert.Equal("Этап «Поиск»", assignedFirstStep.StepHeading);
        Assert.Equal("Линия «Главная линия»", assignedFirstStep.LineHeading);
        Assert.Equal("Идан начинает поиски источника магии.", assignedFirstStep.StepDescriptionText);
        var emptyStep = new ScriSelfStudio.App.Shell.SceneStepEntry(
            Guid.NewGuid(),
            "Главная линия",
            "Этап без описания",
            null,
            null,
            Avalonia.Media.Brushes.Gray,
            (_, _) => Task.CompletedTask,
            _ => { });
        Assert.False(emptyStep.HasStepDescription);
        Assert.Equal("Описание этапа не задано.", emptyStep.StepDescriptionText);
        var character = contextPanel.Characters[0];
        Assert.Equal("Этап арки: Сопротивление", character.StepLabel);
        Assert.Equal("Идан отказывается покинуть родной город.", character.StepDescription);
        character.ToggleDescriptionCommand.Execute(null);
        Assert.True(character.IsDescriptionExpanded);
        Assert.True(assignedFirstStep.IsDescriptionExpanded);
        character.ToggleDescriptionCommand.Execute(null);
        Assert.False(character.IsDescriptionExpanded);

        contextPanel.SelectedStepOption = contextPanel.StepOptions.First(option => option.Title == "Развязка");
        await contextPanel.AddStepCommand.ExecuteAsync(null);
        Assert.Equal(2, contextPanel.AssignedSteps.Count);
        Assert.True(contextPanel.AssignedSteps[0].IsDescriptionExpanded);
        contextPanel.AssignedSteps[1].ToggleDescriptionCommand.Execute(null);
        Assert.True(contextPanel.AssignedSteps[1].IsDescriptionExpanded);
        contextPanel.AssignedSteps[1].ToggleDescriptionCommand.Execute(null);
        Assert.False(contextPanel.AssignedSteps[1].IsDescriptionExpanded);

        character = contextPanel.Characters[0];
        character.ToggleDescriptionCommand.Execute(null);
        Assert.True(character.IsDescriptionExpanded);
        Assert.Equal("Идан отказывается покинуть родной город.", character.StepDescription);
        contextPanel.CollapseDescriptions();
        Assert.False(character.IsDescriptionExpanded);
        Assert.All(contextPanel.AssignedSteps, step => Assert.False(step.IsDescriptionExpanded));

        var editor = provider.GetRequiredService<EditorViewModel>();
        editor.Text = "Первая строка рукописи.\nВторая строка с «ёжиком» и emoji 🚀.\n\nЕщё абзац.";
        if (cabinet)
        {
            editor.Text = "К утру дождь закончился. Город ещё спал, и только в окне напротив горел свет. Идан остановился у двери мастерской, прислушиваясь к тишине. За этой дверью начиналось всё, что он столько лет откладывал.\n\n"
                + "На столе лежала раскрытая тетрадь. Незнакомый почерк заполнял страницы неторопливо, оставляя широкие поля. Между строк попадались наброски улиц, названия деревень и короткие замечания о ветре. Это было похоже на путевые записи человека, который никуда не спешил.\n\n"
                + "Лея нашла его у окна. Она не стала спрашивать, почему он пришёл так рано, а просто поставила рядом две чашки. От горячего чая поднимался пар, и на мгновение Идану показалось, что в комнате стало светлее.\n\n"
                + "— Начнём с самого простого, — сказала она. — Расскажи, что ты помнишь.\n\n"
                + "Он посмотрел на первую страницу. Вчера эти слова казались чужими, но теперь в них проступал знакомый ритм. Где-то за городом ждала дорога, которую однажды уже прошёл его отец. Идан взял карандаш и написал на свободном поле одно слово: «Вернуться».";
        }
        Dispatcher.UIThread.RunJobs();
        // The active scene uses the rich-text editor and synchronizes with the document.
        Assert.NotEmpty(editor.Blocks);
        var block = editor.Blocks.FirstOrDefault(candidate => candidate.IsActive);
        Assert.NotNull(block);
        Assert.Equal(editor.Text, block!.Text);
        Assert.Equal(editor.Text, editor.Engine.PlainText);

        var sceneField = FindRichEditor(window);
        Assert.NotNull(sceneField);
        Assert.Contains(editor.Text, sceneField!.GetPlainText().Replace("\r\n", "\n"));
        Assert.Equal(
            editor.Blocks.Count - 1,
            sceneField.Document!.Blocks.OfType<DividerBlock>().Count());
        Assert.True(sceneField.Bounds.Height > 0, $"Высота поля сцены равна 0 (bounds={sceneField.Bounds}).");
        Render(window, "03-editor");

        // Режим «Полный холст»: боковые панели скрыты, «бумага» растягивается
        // на всю ширину с отступами по 20px — кадр для визуальной приёмки.
        var shell = provider.GetRequiredService<ScriSelfStudio.App.Shell.ShellViewModel>();
        if (cabinet)
        {
            var originalDocument = sceneField.Document;
            var originalText = sceneField.GetPlainText();
            editor.IncreaseReadingZoomCommand.Execute(null);
            editor.DecreaseReadingZoomCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.Same(originalDocument, sceneField.Document);
            Assert.Equal(originalText, sceneField.GetPlainText());
            Assert.False(shell.ShowScenePanel);
            Assert.True(shell.ShowBookTree);
            shell.ToggleScenePanelCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.True(shell.ShowScenePanel);
            Render(window, "03c-editor-context");
            shell.ToggleScenePanelCommand.Execute(null);
            editor.ToggleToolbarMoreCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Render(window, "03d-editor-formatting");
            editor.ToggleToolbarMoreCommand.Execute(null);
            foreach (var width in new[] { 800, 1024, 1280, 1920 })
            {
                window.Width = width;
                window.Height = width == 800 ? 600 : 768;
                Dispatcher.UIThread.RunJobs();
                Render(window, $"03e-editor-{width}");
                Assert.True(sceneField.Bounds.Width >= 360, $"Editor width {sceneField.Bounds.Width} at window {width}");
                shell.ToggleScenePanelCommand.Execute(null);
                Render(window, $"03f-context-{width}");
                Assert.True(sceneField.Bounds.Width >= 300, $"Context leaves editor width {sceneField.Bounds.Width} at {width}");
                shell.ToggleScenePanelCommand.Execute(null);
            }
            window.Width = 1440;
            window.Height = 768;
            Assert.Same(originalDocument, sceneField.Document);
            Assert.Equal(originalText, sceneField.GetPlainText());
            // Switching layout must not replace the document or lose the active editing state.
            await shell.SelectThemeCommand.ExecuteAsync(shell.Themes[0]);
            await shell.SelectThemeCommand.ExecuteAsync(shell.Themes.Single(option => option.Value == theme));
            Dispatcher.UIThread.RunJobs();
            Assert.Same(originalDocument, sceneField.Document);
            Assert.Equal(originalText, sceneField.GetPlainText());
            shell.IsScenePanelRequested = true;
        }
        shell.IsFullCanvas = true;
        Dispatcher.UIThread.RunJobs();
        Render(window, "03b-editor-full-canvas");
        Assert.False(shell.ShowBookTree);
        Assert.False(shell.ShowScenePanel);
        // Панель подсказок по сцене обязана исчезнуть из раскладки холста.
        var scenePanel = window.GetVisualDescendants().OfType<ContentControl>()
            .FirstOrDefault(control => control.Content is ScriSelfStudio.App.Shell.SceneContextViewModel);
        Assert.NotNull(scenePanel);
        Assert.False(scenePanel!.IsVisible);
        shell.IsFullCanvas = false;
        Dispatcher.UIThread.RunJobs();
        Assert.True(shell.ShowBookTree);
        Assert.True(scenePanel.IsVisible);


        launcher.OpenCharactersCommand.Execute(null);
        Assert.Equal(cabinet, shell.ShowBookTree);
        Assert.False(shell.ShowScenePanel);
        var characters = provider.GetRequiredService<CharactersViewModel>();
        characters.NewCharacterCommand.Execute(null);
        characters.Draft.Name = "Анна";
        characters.Draft.Age = "25 лет";
        characters.Draft.FalseBelief = "Деньги решают всё";
        characters.Draft.TagsText = "маг, предатель";
        Dispatcher.UIThread.RunJobs();
        Render(window, "05-characters");

        launcher.OpenTimelineCommand.Execute(null);
        var timeline = provider.GetRequiredService<TimelineViewModel>();

        // Кадр сверяется с макетом «Сюжетные линии», поэтому данные повторяют
        // макет: линия с четырьмя этапами и ещё три строки списка — видно и
        // выбранный пункт, и разделители, и ленту этапов.
        timeline.NewLineCommand.Execute(null);
        timeline.Draft.Name = "Главная сюжетная линия";
        timeline.Draft.Description = "Поиск источника магии";
        timeline.Draft.Goal = "Найти источник магии.";
        await timeline.SaveLineCommand.ExecuteAsync(null);

        string[] stepTitles = ["Начало", "Призыв", "Поиск", "Источник найден"];
        string[] stepTexts =
        [
            "Идан узнаёт о существовании источника магии.",
            "Появляется причина отправиться на поиски.",
            "Идан следует по следам источника.",
            "Идан достигает цели.",
        ];
        for (var index = 0; index < stepTitles.Length; index++)
        {
            timeline.AddStepCommand.Execute(null);
            timeline.SelectedStep!.Title = stepTitles[index];
            timeline.SelectedStep!.Description = stepTexts[index];
        }

        await timeline.SaveLineCommand.ExecuteAsync(null);

        (string Name, string Summary)[] otherLines =
        [
            ("Любовная линия", "Идан и Лея"),
            ("Тайна города", "Загадка прошлого"),
            ("Исчезновение", "Пропавшие артефакты"),
        ];
        foreach (var (name, summary) in otherLines)
        {
            timeline.NewLineCommand.Execute(null);
            timeline.Draft.Name = name;
            timeline.Draft.Description = summary;
            await timeline.SaveLineCommand.ExecuteAsync(null);
        }

        timeline.SelectedLine = timeline.PlotLines.First(line => line.Name == "Главная сюжетная линия");
        Dispatcher.UIThread.RunJobs();
        Render(window, "06-timeline");

        shell.OpenSettingsCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.False(shell.ShowBookTree);
        Assert.False(shell.ShowScenePanel);
        Assert.False(shell.ShowStatusBar);
        Render(window, "07-settings");

        if (cabinet)
        {
            window.Width = 800;
            Render(window, "07b-settings-800");
            var themeSettings = provider.GetRequiredService<SettingsViewModel>();
            themeSettings.SelectedSection = themeSettings.Sections.Single(section => section.Section == SettingsSection.Appearance);
            Render(window, "07c-appearance-800");
            window.Width = 1440;
            Render(window, "07d-appearance");
            if (Environment.GetEnvironmentVariable("SCRISELF_RENDER_DIR") is { Length: > 0 } renderDirectory)
            {
                await shell.FlushPendingEditsAsync();
                var saved = await provider.GetRequiredService<IProjectService>().SaveAsAsync(Path.Combine(renderDirectory,
                    theme == AppTheme.Jam ? "jam" : "cabinet", "Рабочее место — пример.scri"));
                Assert.True(saved.IsSuccess, saved.Error?.UserMessage);
            }
        }

        window.Close();
    }

    /// <summary>
    /// Добавляет открытому проекту книгу, главы и сцены и возвращает его снимок.
    /// Структура настоящая: счётчики карточек берутся из неё, а не выдуманы.
    /// </summary>
    private static async Task<ScriSelfStudio.Contracts.Dtos.ProjectDto> BuildStructureAsync(
        IServiceProvider provider,
        int chapters,
        int scenes)
    {
        var projects = provider.GetRequiredService<IProjectService>();
        var architecture = provider.GetRequiredService<IBookArchitectureService>();
        var project = projects.Current ?? throw new InvalidOperationException("Проект не открыт.");

        var book = (await architecture.CreateBookAsync(new CreateBookRequest
        {
            SeriesId = project.Series[0].Id,
            Title = "Книга 1",
        })).Require();

        var bookId = book.Series[0].Books[0].Id;
        for (var chapter = 0; chapter < chapters; chapter++)
        {
            project = (await architecture.CreateChapterAsync(new CreateChapterRequest
            {
                BookId = bookId,
                Title = $"Глава {chapter + 1}",
            })).Require();
        }

        var chapterId = project.Series[0].Books[0].Chapters[0].Id;
        for (var scene = 0; scene < scenes; scene++)
        {
            project = (await architecture.CreateSceneAsync(new CreateSceneRequest
            {
                ChapterId = chapterId,
                Title = $"Сцена {scene + 1}",
            })).Require();
        }

        return project;
    }

    /// <summary>
    /// Наполняет список последних проектов реальными файлами со структурой:
    /// лаунчер должен выглядеть как макет, а не как пустой список.
    /// </summary>
    private static async Task SeedShowcaseProjectsAsync(IServiceProvider provider, TempWorkspace workspace)
    {
        var projects = provider.GetRequiredService<IProjectService>();
        var settings = provider.GetRequiredService<IAppSettingsService>();

        (string Name, int Chapters, int Scenes, string Description)[] showcase =
        [
            ("Наследие", 8, 4, "Семейная сага о цене наследия."),
            ("Тени города", 5, 3, "Детектив в городе, где все что-то скрывают."),
            ("Второй шанс", 4, 3, "История о праве начать заново."),
            ("Звёздный путь", 6, 4, "Фантастика о дороге домой."),
        ];

        for (var index = 0; index < showcase.Length; index++)
        {
            var (name, chapters, scenes, description) = showcase[index];
            var showPath = workspace.FilePath(name + ".scri");
            var created = await projects.CreateAsync(new CreateProjectRequest { FilePath = showPath, Name = name });
            _ = created.Require();

            var project = await BuildStructureAsync(provider, chapters, scenes);
            var bookCount = project.Series.Sum(series => series.Books.Count);
            var chapterCount = project.Series.SelectMany(series => series.Books).Sum(item => item.Chapters.Count);
            var sceneCount = project.Series
                .SelectMany(series => series.Books)
                .SelectMany(item => item.Chapters)
                .Sum(item => item.Scenes.Count);

            await settings.AddRecentProjectAsync(new RecentProjectEntry
            {
                FilePath = showPath,
                Name = name,
                LastOpenedAt = DateTimeOffset.UtcNow.AddDays(-index - 1),
                Description = description,
                BookCount = bookCount,
                ChapterCount = chapterCount,
                SceneCount = sceneCount,
                UpdatedAt = DateTimeOffset.Now.AddDays(-index - 1).AddHours(-2),
            });

            // Создание требует закрытого проекта — освобождаем перед следующим.
            // Структура уже сохранена, изменения в памяти можно отбросить.
            await projects.CloseAsync(discardChanges: true);
        }
    }

    /// <summary>
    /// Ввод в rich-text редакторе не должен падать, а rich-документ и модель — расходиться.
    /// </summary>
    [AvaloniaFact]
    public async Task Typing_into_editor_does_not_throw()
    {
        using var workspace = new TempWorkspace();
        var path = workspace.FilePath("Рукопись.scri");
        var dialogs = new StubFileDialogService { CreatePath = path, PromptResult = "Элемент" };
        using var provider = ServiceProviderFactory.Create(workspace.Root, dialogs);
        var window = provider.GetRequiredService<MainWindow>();
        var launcher = provider.GetRequiredService<LauncherViewModel>();
        await launcher.InitializeAsync();
        await launcher.CreateProjectCommand.ExecuteAsync(null);
        window.Show();

        launcher.OpenArchitectureCommand.Execute(null);
        var architecture = provider.GetRequiredService<ArchitectureViewModel>();
        architecture.Selected = architecture.Roots[0];
        await architecture.CreateBookCommand.ExecuteAsync(null);
        architecture.Selected = architecture.Roots[0].Children[0];
        await architecture.CreateChapterCommand.ExecuteAsync(null);
        architecture.Selected = architecture.Roots[0].Children[0].Children[0];
        await architecture.CreateSceneCommand.ExecuteAsync(null);
        architecture.Selected = architecture.Roots[0].Children[0].Children[0].Children[0];
        await architecture.OpenSceneCommand.ExecuteAsync(null);
        var editor = provider.GetRequiredService<EditorViewModel>();
        editor.Text = "Первая строка рукописи.\nВторая строка.";
        Dispatcher.UIThread.RunJobs();

        var richEditor = FindRichEditor(window);
        richEditor.FocusDocumentEnd();
        Dispatcher.UIThread.RunJobs();

        var cases = new (string Name, string Text)[]
        {
            ("ввод латиницы", "Z"),
            ("ввод кириллицы", "Ёжик"),
            ("ввод emoji", "🚀"),
            ("ввод пробела", " "),
            ("ввод табуляции", "\t"),
            ("перевод строки", "\n"),
        };

        foreach (var (name, act) in cases)
        {
            try
            {
                richEditor.InsertText(act);
                Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                Dispatcher.UIThread.RunJobs();
            }
            catch (Exception ex)
            {
                Assert.Fail($"Сценарий '{name}' упал: {ex}");
            }

            var richText = WithoutSceneHeaders(richEditor.GetPlainText());
            if (editor.Text != richText)
            {
                Assert.Fail($"Сценарий '{name}': модель и rich editor разошлись.\nмодель: {editor.Text}\nrich editor: {richText}");
            }

            if (editor.Engine.PlainText != richText)
            {
                Assert.Fail($"Сценарий '{name}': документ и rich editor разошлись.\nдокумент: {editor.Engine.PlainText}\nrich editor: {richText}");
            }
        }

        window.Close();
    }

    private static RichEditor FindRichEditor(Visual root)
    {
        return root.GetVisualDescendants().OfType<RichEditor>().First();
    }

    /// <summary>
    /// Служебные заголовки сцен живут только в отображаемом FlowDocument:
    /// в модель сцены они не попадают, поэтому тесты сравнивают текст без них.
    /// </summary>
    private static string WithoutSceneHeaders(string text) =>
        string.Join('\n', text.Replace("\r\n", "\n").Split('\n')
            .Where(line => !line.StartsWith('\u200B')));

    /// <summary>
    /// Карта книги с наполнением: столбцы глав, номера сцен, правая панель «Сцена»
    /// и полоса «Быстрый обзор» (ТЗ п. 27). Кадр — для визуальной приёмки.
    /// </summary>
    [AvaloniaFact]
    public async Task Book_map_renders_columns_and_scene_details()
    {
        using var workspace = new TempWorkspace();
        var path = workspace.FilePath("Карта.scri");
        var dialogs = new StubFileDialogService { CreatePath = path, PromptResult = "Элемент" };
        using var provider = ServiceProviderFactory.Create(workspace.Root, dialogs);
        var window = provider.GetRequiredService<MainWindow>();
        var launcher = provider.GetRequiredService<LauncherViewModel>();
        await launcher.InitializeAsync();
        await launcher.CreateProjectCommand.ExecuteAsync(null);

        var projects = provider.GetRequiredService<IProjectService>();
        var architecture = provider.GetRequiredService<IBookArchitectureService>();
        var seriesId = architecture.GetSeries()[0].Id;
        var book = (await architecture.CreateBookAsync(new CreateBookRequest
        {
            SeriesId = seriesId,
            Title = "Пепел и Серебро",
        })).Require();
        var bookId = book.Series[0].Books[0].Id;

        string[] chapterTitles = ["Пролог", "Путь", "Финал"];
        int[] sceneCounts = [2, 3, 2];
        Guid selectedSceneId = default;
        for (var chapter = 0; chapter < chapterTitles.Length; chapter++)
        {
            var snapshot = (await architecture.CreateChapterAsync(new CreateChapterRequest
            {
                BookId = bookId,
                Title = chapterTitles[chapter],
                Description = $"Описание главы «{chapterTitles[chapter]}».",
            })).Require();

            var chapterId = snapshot.Series[0].Books[0].Chapters[chapter].Id;
            for (var scene = 0; scene < sceneCounts[chapter]; scene++)
            {
                var withScene = (await architecture.CreateSceneAsync(new CreateSceneRequest
                {
                    ChapterId = chapterId,
                    Title = $"{chapterTitles[chapter]}: сцена {scene + 1}",
                })).Require();

                var created = withScene.Series[0].Books[0].Chapters[chapter].Scenes[scene];
                if (chapter == 0 && scene == 1)
                {
                    selectedSceneId = created.Id;
                }

                await projects.UpdateSceneAsync(new UpdateSceneRequest
                {
                    SceneId = created.Id,
                    Description = "Краткое содержание сцены для правой панели карты.",
                    Status = chapter == 0 ? SceneStatus.Draft : SceneStatus.Idea,
                    IsKey = chapter == 0 && scene == 1,
                    IsLocked = chapter == 0 && scene == 0,
                    Notes = chapter == 0 && scene == 1 ? "Связать сцену с артефактом." : null,
                });
            }
        }

        var withIdan = (await architecture.SaveCharacterAsync(new CharacterDto
        {
            Name = "Идан",
            Role = CharacterRole.Protagonist,
            ExternalGoal = "Найти источник магии",
        })).Require();
        var idanId = withIdan.Characters.Single(c => c.Name == "Идан").Id;
        var withLeia = (await architecture.SaveCharacterAsync(new CharacterDto
        {
            Name = "Лея",
            Role = CharacterRole.Ally,
            Description = "Поддерживает Идана.",
        })).Require();
        var leiaId = withLeia.Characters.Single(c => c.Name == "Лея").Id;

        await architecture.SavePlotLineAsync(new PlotLineDto
        {
            Name = "Главная линия",
            ColorHex = "#4A7DFF",
            Status = PlotLineStatus.InProgress,
            SceneIds = [selectedSceneId],
            Steps =
            [
                new PlotLineStepDto
                {
                    Title = "Поиск",
                    Order = 0,
                    SceneIds = [selectedSceneId],
                    Participants =
                    [
                        new StepParticipantDto { CharacterId = idanId },
                        new StepParticipantDto { CharacterId = leiaId },
                    ],
                },
            ],
        });
        await architecture.SavePlotLineAsync(new PlotLineDto
        {
            Name = "Линия героя",
            ColorHex = "#C08A2E",
            Status = PlotLineStatus.Done,
            SceneIds = [selectedSceneId],
        });
        var withMira = (await architecture.SaveCharacterAsync(new CharacterDto
        {
            Name = "Мира",
            Role = CharacterRole.Secondary,
        })).Require();
        var miraId = withMira.Characters.Single(character => character.Name == "Мира").Id;

        launcher.OpenArchitectureCommand.Execute(null);
        var map = provider.GetRequiredService<ArchitectureViewModel>();
        map.Refresh();
        map.SelectNodeCommand.Execute(map.MapColumns[0].Scenes[1].Node);
        Dispatcher.UIThread.RunJobs();
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(3, map.MapColumns.Count);
        Assert.Equal("1.2", map.MapColumns[0].Scenes[1].Number);
        Assert.Equal(3, map.ChapterOverview.Count);
        Assert.True(map.HasSceneDetails);
        Assert.True(map.SceneDetails!.HasNotes);
        Assert.True(map.SceneDetails.IsKey);
        Assert.Equal(2, map.SceneDetails.Lines.Count);
        Assert.Equal(2, map.SceneDetails.Characters.Count);
        Assert.Single(map.SceneDetails.CharacterChoices);
        Assert.Equal(miraId, map.SceneDetails.CharacterChoices[0].Id);
        Assert.Equal("Главная линия", map.SceneDetails.Lines[0].Name);
        Assert.Equal("Краткое содержание сцены для правой панели карты.", map.SceneDetails.Description);
        Assert.True(map.MapColumns[0].Scenes[0].IsLocked);
        Assert.False(map.SceneDetails.IsLocked);
        Assert.Contains(map.PlotLineLegend, marker => marker.IsClosed);
        Assert.True(map.ShowLegend);
        Render(window, "20-map");

        var sceneDetails = map.SceneDetails!;
        sceneDetails.Description = "Описание редактируется прямо в панели сцены.";
        await sceneDetails.SaveDescriptionCommand.ExecuteAsync(null);
        Assert.Equal(
            "Описание редактируется прямо в панели сцены.",
            projects.Current!.Series[0].Books[0].Chapters[0].Scenes[1].Description);

        sceneDetails = map.SceneDetails!;
        sceneDetails.SelectedCharacterChoice = sceneDetails.CharacterChoices[0];
        await sceneDetails.AddCharacterCommand.ExecuteAsync(null);
        Assert.Equal(3, map.SceneDetails!.Characters.Count);

        map.ToggleOverviewCommand.Execute(null);
        Assert.False(map.IsOverviewExpanded);
        Assert.False(map.ShowLegend);
        Render(window, "21-map-overview-collapsed");

        map.ToggleOverviewCommand.Execute(null);
        Assert.True(map.IsOverviewExpanded);

        window.Close();
    }

    [AvaloniaFact]
    public async Task Cloud_panel_renders_connection_form()
    {
        using var workspace = new TempWorkspace();
        using var provider = ServiceProviderFactory.Create(workspace.Root);
        var window = provider.GetRequiredService<MainWindow>();
        var launcher = provider.GetRequiredService<LauncherViewModel>();
        await launcher.InitializeAsync();
        window.Width = 1440;
        window.Height = 768;
        window.Show();

        launcher.OpenCloudCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.True(launcher.IsCloudOpen);
        Assert.False(launcher.IsCloudConnected);
        Render(window, "22-cloud-connect");

        window.Close();
    }

    private static void Render(Window window, string name)
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();

        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);

        var directory = Environment.GetEnvironmentVariable("SCRISELF_RENDER_DIR");
        if (string.IsNullOrEmpty(directory))
        {
            return;
        }

        if (window.DataContext is ScriSelfStudio.App.Shell.ShellViewModel { IsJamTheme: true })
            directory = Path.Combine(directory, "jam");
        else if (window.DataContext is ScriSelfStudio.App.Shell.ShellViewModel { IsCabinetTheme: true })
            directory = Path.Combine(directory, "cabinet");
        else if (window.DataContext is ScriSelfStudio.App.Shell.ShellViewModel { SelectedTheme.Value: AppTheme.GreenStart })
            directory = Path.Combine(directory, "green-start");
        Directory.CreateDirectory(directory);
        frame!.Save(Path.Combine(directory, name + ".png"), PngBitmapEncoderOptions.Default);
    }
}
