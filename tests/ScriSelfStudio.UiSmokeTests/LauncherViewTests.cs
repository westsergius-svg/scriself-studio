using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using ScriSelfStudio.App.Shell;
using ScriSelfStudio.App.Views;
using ScriSelfStudio.Contracts.Books;
using ScriSelfStudio.Contracts.Cloud;
using ScriSelfStudio.Contracts.Navigation;
using ScriSelfStudio.Contracts.Projects;
using ScriSelfStudio.Contracts.Settings;
using ScriSelfStudio.Contracts.Storage;
using ScriSelfStudio.Launcher;
using ScriSelfStudio.Core.Updates;
using ScriSelfStudio.ArchitectureUI;
using ScriSelfStudio.Persistence.FileSystem;
using ScriSelfStudio.Presentation.Dialogs;
using ScriSelfStudio.TestSupport;

namespace ScriSelfStudio.UiSmokeTests;

public sealed class LauncherViewTests
{
    [AvaloniaFact]
    public async Task Cloud_panel_switches_to_google_drive_form()
    {
        using var workspace = new TempWorkspace();
        using var provider = ServiceProviderFactory.Create(workspace.Root);
        var launcher = provider.GetRequiredService<LauncherViewModel>();
        await launcher.InitializeAsync();

        launcher.SelectedCloudProvider = launcher.CloudProviders
            .Single(cloudProvider => cloudProvider.Kind == CloudProviderKind.GoogleDrive);

        Assert.True(launcher.IsGoogleCloud);
        Assert.False(launcher.IsYandexCloud);
        Assert.True(launcher.ShowGoogleConnectForm);
        Assert.False(launcher.ShowYandexConnectForm);
        Assert.Contains("Google Drive", launcher.CloudProviderTitle);
    }

    /// <summary>
    /// Создаёт проект отдельным контейнером: файл нужен на диске до открытия,
    /// чтобы сценарий шёл через настоящий путь «открыть существующий проект».
    /// </summary>
    private static async Task CreateProjectFileAsync(string root, string path, string name)
    {
        using var provider = ServiceProviderFactory.Create(root);
        var projects = provider.GetRequiredService<Contracts.Projects.IProjectService>();
        var created = await projects.CreateAsync(new Contracts.Projects.CreateProjectRequest
        {
            FilePath = path,
            Name = name,
        });
        Assert.True(created.IsSuccess, created.Error?.TechnicalDetails);
    }
    [AvaloniaFact]
    public void Main_window_shows_launcher_screen_without_hollow_buttons()
    {
        using var workspace = new TempWorkspace();
        using var provider = ServiceProviderFactory.Create(workspace.Root);

        var window = provider.GetRequiredService<MainWindow>();
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var shell = Assert.IsType<ShellViewModel>(window.DataContext);
        Assert.IsType<LauncherViewModel>(shell.Current);

        var launcherView = window.GetVisualDescendants().OfType<LauncherView>().FirstOrDefault();
        Assert.NotNull(launcherView);

        var buttons = launcherView!.GetVisualDescendants().OfType<Button>().ToList();
        Assert.NotEmpty(buttons);
        Assert.All(buttons, button => Assert.NotNull(button.Command));

        window.Close();
    }

    [AvaloniaFact]
    public async Task Startup_check_offers_a_new_version_from_the_update_channel()
    {
        using var workspace = new TempWorkspace();
        var dialogs = new StubFileDialogService { ConfirmResult = false };
        var updates = new FakeUpdateService
        {
            CurrentVersion = "1.0.0",
            Latest = new UpdateInfo(
                [1, 1, 0],
                "1.1.0",
                "ScriSelfStudio-Setup-1.1.0.exe",
                "https://example.test/updates/ScriSelfStudio-Setup-1.1.0.exe",
                string.Empty,
                "manifest"),
        };
        using var provider = ServiceProviderFactory.Create(workspace.Root, dialogs, updates);
        var launcher = provider.GetRequiredService<LauncherViewModel>();
        await launcher.InitializeAsync();

        await launcher.CheckForUpdatesOnStartupAsync();

        Assert.Equal(1, updates.Checks);
        Assert.Single(dialogs.Confirmations);
        Assert.Contains("1.1.0", dialogs.Confirmations[0]);
    }

    [AvaloniaFact]
    public async Task Create_project_command_creates_file_and_remembers_it()
    {
        using var workspace = new TempWorkspace();
        var projectPath = workspace.FilePath("Новый проект.scri");
        var dialogs = new StubFileDialogService { CreatePath = projectPath };
        using var provider = ServiceProviderFactory.Create(workspace.Root, dialogs);
        var launcher = provider.GetRequiredService<LauncherViewModel>();
        await launcher.InitializeAsync();

        await launcher.CreateProjectCommand.ExecuteAsync(null);

        Assert.True(File.Exists(projectPath));
        Assert.NotNull(launcher.CurrentProject);
        Assert.Equal("Новый проект", launcher.CurrentProject!.Name);
        Assert.False(launcher.HasError);
        Assert.True(launcher.IsProjectOpen);
        Assert.Single(launcher.RecentProjects);
        Assert.Equal(projectPath, launcher.RecentProjects[0].FilePath);
    }

    [AvaloniaFact]
    public async Task Flushing_project_updates_persisted_recent_project_summary()
    {
        using var workspace = new TempWorkspace();
        var projectPath = workspace.FilePath("Рукопись.scri");
        var dialogs = new StubFileDialogService { CreatePath = projectPath };
        dialogs.PromptResponses.Enqueue("Книга");
        dialogs.PromptResponses.Enqueue("Глава");
        dialogs.PromptResponses.Enqueue("Сцена");
        using var provider = ServiceProviderFactory.Create(workspace.Root, dialogs);
        var launcher = provider.GetRequiredService<LauncherViewModel>();
        var cancellationToken = TestContext.Current.CancellationToken;
        await launcher.InitializeAsync(cancellationToken);
        await launcher.CreateProjectCommand.ExecuteAsync(null);

        var architecture = provider.GetRequiredService<ArchitectureViewModel>();
        architecture.Selected = architecture.Roots[0];
        await architecture.CreateBookCommand.ExecuteAsync(null);
        architecture.Selected = architecture.Roots[0].Children[0];
        await architecture.CreateChapterCommand.ExecuteAsync(null);
        architecture.Selected = architecture.Roots[0].Children[0].Children[0];
        await architecture.CreateSceneCommand.ExecuteAsync(null);

        var shell = provider.GetRequiredService<ShellViewModel>();
        await shell.FlushPendingEditsAsync(cancellationToken);

        var reloadedSettings = new JsonAppSettingsService(workspace.Root);
        var loaded = await reloadedSettings.LoadAsync(cancellationToken);
        Assert.True(loaded.IsSuccess);
        var recent = Assert.Single(reloadedSettings.Current.RecentProjects);
        Assert.Equal(1, recent.BookCount);
        Assert.Equal(1, recent.ChapterCount);
        Assert.Equal(1, recent.SceneCount);

        using var restartedProvider = ServiceProviderFactory.Create(workspace.Root);
        var restartedLauncher = restartedProvider.GetRequiredService<LauncherViewModel>();
        await restartedLauncher.InitializeAsync(cancellationToken);
        var displayedRecent = Assert.Single(restartedLauncher.RecentProjects);
        Assert.Equal(1, displayedRecent.BookCount);
        Assert.Equal(1, displayedRecent.ChapterCount);
        Assert.Equal(1, displayedRecent.SceneCount);
    }

    [Fact]
    public void Prompt_dialog_shows_initial_value_as_hint_and_uses_it_when_confirmed_empty()
    {
        string? result = null;
        var prompt = new PromptDialogViewModel("Новая книга", "Название книги", "Книга", value => result = value);

        Assert.Equal(string.Empty, prompt.Value);
        Assert.Equal("Книга", prompt.InitialValue);
        prompt.ConfirmCommand.Execute(null);

        Assert.Equal("Книга", result);
    }

    [AvaloniaFact]
    public void Rename_prompt_keeps_existing_title_when_a_suffix_is_typed()
    {
        string? result = null;
        var prompt = new PromptDialogViewModel(
            "Переименовать", "Книга", "Пробная книга", value => result = value,
            prefillInitialValue: true);
        var dialog = new PromptDialog { DataContext = prompt };
        dialog.Show();
        Dispatcher.UIThread.RunJobs();

        var input = Assert.Single(dialog.GetVisualDescendants().OfType<TextBox>());
        Assert.True(input.Focus());
        dialog.KeyPress(Key.End, RawInputModifiers.None, PhysicalKey.End, null);
        dialog.KeyRelease(Key.End, RawInputModifiers.None, PhysicalKey.End, null);
        dialog.KeyTextInput(" 2");
        prompt.ConfirmCommand.Execute(null);

        Assert.Equal("Пробная книга 2", result);
        dialog.Close();
    }

    /// <summary>
    /// Только что созданный проект пуст (нет книг и сцен). Раньше он оставался
    /// «неоткрытым»: экран не менялся, потому что открывать в редакторе нечего.
    /// Теперь проект открывается на карте книги, где автор создаёт первую сцену.
    /// </summary>
    [AvaloniaFact]
    public async Task Creating_a_project_opens_the_book_map()
    {
        using var workspace = new TempWorkspace();
        var projectPath = workspace.FilePath("Новый проект.scri");
        var dialogs = new StubFileDialogService { CreatePath = projectPath };
        using var provider = ServiceProviderFactory.Create(workspace.Root, dialogs);
        var launcher = provider.GetRequiredService<LauncherViewModel>();
        await launcher.InitializeAsync();

        await launcher.CreateProjectCommand.ExecuteAsync(null);

        Assert.True(launcher.IsProjectOpen);
        var navigation = provider.GetRequiredService<INavigationService>();
        Assert.Equal(AppScreen.Architecture, navigation.Current);
    }

    /// <summary>
    /// Проект со сценами при открытии сразу показывает первую сцену в редакторе:
    /// открытие не должно терять прежнее поведение редактора.
    /// </summary>
    [AvaloniaFact]
    public async Task Opening_a_project_with_scenes_opens_the_editor()
    {
        using var workspace = new TempWorkspace();
        var projectPath = workspace.FilePath("Роман.scri");
        using (var seed = ServiceProviderFactory.Create(workspace.Root))
        {
            var store = seed.GetRequiredService<IProjectStore>();
            var saved = await store.SaveAsync(new ProjectSaveRequest
            {
                Project = SampleProjects.CreateBookWithScenes(),
                FilePath = projectPath,
                CreateBackup = false,
            });
            Assert.True(saved.IsSuccess, saved.Error?.TechnicalDetails);
        }

        var dialogs = new StubFileDialogService { OpenPath = projectPath };
        using var provider = ServiceProviderFactory.Create(workspace.Root, dialogs);
        var launcher = provider.GetRequiredService<LauncherViewModel>();
        await launcher.InitializeAsync();

        await launcher.OpenProjectCommand.ExecuteAsync(null);

        Assert.False(launcher.HasError, launcher.ErrorMessage);
        var navigation = provider.GetRequiredService<INavigationService>();
        Assert.Equal(AppScreen.Editor, navigation.Current);
    }

    [AvaloniaFact]
    public async Task Creating_a_second_project_saves_and_closes_the_first()
    {
        using var workspace = new TempWorkspace();
        var firstPath = workspace.FilePath("Первый.scri");
        var secondPath = workspace.FilePath("Второй.scri");
        var dialogs = new StubFileDialogService { CreatePath = firstPath };
        using var provider = ServiceProviderFactory.Create(workspace.Root, dialogs);
        var launcher = provider.GetRequiredService<LauncherViewModel>();
        await launcher.InitializeAsync();

        await launcher.CreateProjectCommand.ExecuteAsync(null);
        Assert.True(launcher.IsProjectOpen);
        Assert.Equal("Первый", launcher.CurrentProject!.Name);

        // Второй проект создаётся при уже открытом первом: текущий сохраняется
        // и закрывается, новый становится открытым — без ошибки
        // «Сначала закройте текущий проект».
        dialogs.CreatePath = secondPath;
        await launcher.CreateProjectCommand.ExecuteAsync(null);

        Assert.False(launcher.HasError, launcher.ErrorMessage);
        Assert.Equal("Второй", launcher.CurrentProject!.Name);
        Assert.Equal(secondPath, provider.GetRequiredService<IProjectService>().CurrentFilePath);
        Assert.True(File.Exists(firstPath));
        Assert.True(File.Exists(secondPath));
    }

    [AvaloniaFact]
    public async Task Opening_another_project_while_one_is_open_switches_to_it()
    {
        using var workspace = new TempWorkspace();
        var firstPath = workspace.FilePath("Первый.scri");
        var secondPath = workspace.FilePath("Второй.scri");
        await CreateProjectFileAsync(workspace.Root, firstPath, "Первый");
        await CreateProjectFileAsync(workspace.Root, secondPath, "Второй");

        var dialogs = new StubFileDialogService { OpenPath = firstPath };
        using var provider = ServiceProviderFactory.Create(workspace.Root, dialogs);
        var launcher = provider.GetRequiredService<LauncherViewModel>();
        await launcher.InitializeAsync();

        await launcher.OpenProjectCommand.ExecuteAsync(null);
        Assert.True(launcher.IsProjectOpen);
        Assert.Equal("Первый", launcher.CurrentProject!.Name);

        dialogs.OpenPath = secondPath;
        await launcher.OpenProjectCommand.ExecuteAsync(null);

        Assert.False(launcher.HasError, launcher.ErrorMessage);
        Assert.Equal("Второй", launcher.CurrentProject!.Name);
        Assert.Equal(secondPath, provider.GetRequiredService<IProjectService>().CurrentFilePath);
    }

    [AvaloniaFact]
    public async Task Open_project_command_loads_existing_file()
    {
        using var workspace = new TempWorkspace();
        var projectPath = workspace.FilePath("Проект.scri");

        using (var first = ServiceProviderFactory.Create(workspace.Root))
        {
            var projects = first.GetRequiredService<Contracts.Projects.IProjectService>();
            var created = await projects.CreateAsync(new Contracts.Projects.CreateProjectRequest
            {
                FilePath = projectPath,
                Name = "Проект",
            });
            Assert.True(created.IsSuccess, created.Error?.TechnicalDetails);
        }

        var dialogs = new StubFileDialogService { OpenPath = projectPath };
        using var provider = ServiceProviderFactory.Create(workspace.Root, dialogs);
        var launcher = provider.GetRequiredService<LauncherViewModel>();
        await launcher.InitializeAsync();

        await launcher.OpenProjectCommand.ExecuteAsync(null);

        Assert.NotNull(launcher.CurrentProject);
        Assert.Equal("Проект", launcher.CurrentProject!.Name);
        Assert.False(launcher.HasError);
    }

    /// <summary>
    /// Кнопка «Закрыть проект» сохраняет правки сама: подтверждение не
    /// спрашивается, а данные попадают на диск.
    /// </summary>
    [AvaloniaFact]
    public async Task Close_project_saves_unsaved_changes_without_asking()
    {
        using var workspace = new TempWorkspace();
        var projectPath = workspace.FilePath("Проект.scri");
        var dialogs = new StubFileDialogService { CreatePath = projectPath };
        using var provider = ServiceProviderFactory.Create(workspace.Root, dialogs);
        var projects = provider.GetRequiredService<Contracts.Projects.IProjectService>();
        var launcher = provider.GetRequiredService<LauncherViewModel>();
        await launcher.InitializeAsync();
        await launcher.CreateProjectCommand.ExecuteAsync(null);

        projects.Mutate(project =>
        {
            project.Name = "Изменён";
            return Contracts.Errors.Result.Success;
        });
        launcher.SyncProjectState();

        await launcher.CloseProjectCommand.ExecuteAsync(null);

        Assert.Empty(dialogs.Confirmations);
        Assert.Null(launcher.CurrentProject);
        Assert.False(launcher.IsProjectOpen);

        // Правка записана до закрытия: повторное открытие её видит.
        var reopened = await projects.OpenAsync(projectPath);
        Assert.True(reopened.IsSuccess, reopened.Error?.TechnicalDetails);
        Assert.Equal("Изменён", projects.Current!.Name);
    }

    /// <summary>
    /// Главный сценарий: при открытии другого проекта текущий сохраняется и
    /// закрывается, а не отвергается ошибкой «Сначала закройте текущий проект».
    /// </summary>
    [AvaloniaFact]
    public async Task Opening_another_project_saves_and_closes_the_current_one()
    {
        using var workspace = new TempWorkspace();
        var firstPath = workspace.FilePath("Первый.scri");
        var secondPath = workspace.FilePath("Второй.scri");
        await CreateProjectFileAsync(workspace.Root, secondPath, "Второй");

        var dialogs = new StubFileDialogService { CreatePath = firstPath };
        using var provider = ServiceProviderFactory.Create(workspace.Root, dialogs);
        var projects = provider.GetRequiredService<Contracts.Projects.IProjectService>();
        var launcher = provider.GetRequiredService<LauncherViewModel>();
        await launcher.InitializeAsync();

        await launcher.CreateProjectCommand.ExecuteAsync(null);
        Assert.Equal("Первый", launcher.CurrentProject!.Name);

        projects.Mutate(project =>
        {
            project.Name = "Первый изменён";
            return Contracts.Errors.Result.Success;
        });
        launcher.SyncProjectState();

        // Теперь открываем другой проект.
        dialogs.OpenPath = secondPath;
        await launcher.OpenProjectCommand.ExecuteAsync(null);

        Assert.False(launcher.HasError, launcher.ErrorMessage);
        Assert.Equal("Второй", launcher.CurrentProject!.Name);
        Assert.Equal(secondPath, launcher.CurrentProject.FilePath);

        // Первый проект сохранён и закрыт — открывается заново без потерь.
        await projects.CloseAsync(discardChanges: true);
        var reopened = await projects.OpenAsync(firstPath);
        Assert.True(reopened.IsSuccess, reopened.Error?.TechnicalDetails);
        Assert.Equal("Первый изменён", projects.Current!.Name);
    }

    /// <summary>Создание нового проекта при открытом тоже не должно упираться в ошибку.</summary>
    [AvaloniaFact]
    public async Task Creating_another_project_closes_the_current_one()
    {
        using var workspace = new TempWorkspace();
        var firstPath = workspace.FilePath("Первый.scri");
        var secondPath = workspace.FilePath("Второй.scri");

        var dialogs = new StubFileDialogService { CreatePath = firstPath };
        using var provider = ServiceProviderFactory.Create(workspace.Root, dialogs);
        var launcher = provider.GetRequiredService<LauncherViewModel>();
        await launcher.InitializeAsync();

        await launcher.CreateProjectCommand.ExecuteAsync(null);
        Assert.Equal("Первый", launcher.CurrentProject!.Name);

        dialogs.CreatePath = secondPath;
        await launcher.CreateProjectCommand.ExecuteAsync(null);

        Assert.False(launcher.HasError, launcher.ErrorMessage);
        Assert.Equal("Второй", launcher.CurrentProject!.Name);
        Assert.Equal(2, launcher.RecentProjects.Count);
    }

    [AvaloniaFact]
    public async Task Error_is_reported_when_project_file_is_corrupt()
    {
        using var workspace = new TempWorkspace();
        var corruptPath = workspace.FilePath("Битый.scri");
        await File.WriteAllTextAsync(corruptPath, "это не проект");

        var dialogs = new StubFileDialogService { OpenPath = corruptPath };
        using var provider = ServiceProviderFactory.Create(workspace.Root, dialogs);
        var launcher = provider.GetRequiredService<LauncherViewModel>();
        await launcher.InitializeAsync();

        await launcher.OpenProjectCommand.ExecuteAsync(null);

        Assert.True(launcher.HasError);
        Assert.Null(launcher.CurrentProject);
        Assert.Single(dialogs.Errors);
    }

    [AvaloniaFact]
    public async Task Card_of_open_project_shows_live_structure_summary()
    {
        using var workspace = new TempWorkspace();
        var projectPath = workspace.FilePath("Живой проект.scri");
        var dialogs = new StubFileDialogService { CreatePath = projectPath };
        using var provider = ServiceProviderFactory.Create(workspace.Root, dialogs);
        var launcher = provider.GetRequiredService<LauncherViewModel>();
        await launcher.InitializeAsync();
        await launcher.CreateProjectCommand.ExecuteAsync(null);

        await AddStructureAsync(provider, chapterCount: 2, sceneCount: 3);

        // Карточка открытого проекта обновляется вслед за структурой: счётчики
        // берутся не из кэша настроек, а из живого состояния проекта.
        Assert.NotNull(launcher.SelectedProject);
        Assert.Equal(projectPath, launcher.SelectedProject!.FilePath);
        Assert.Equal(1, launcher.SelectedProject.BookCount);
        Assert.Equal(2, launcher.SelectedProject.ChapterCount);
        Assert.Equal(3, launcher.SelectedProject.SceneCount);
        Assert.Contains("1 книга", launcher.SelectedProject.SummaryText);
        Assert.Contains("2 главы", launcher.SelectedProject.SummaryText);
        Assert.Contains("3 сцены", launcher.SelectedProject.SummaryText);
    }

    [AvaloniaFact]
    public async Task Search_filters_visible_project_cards()
    {
        using var workspace = new TempWorkspace();
        using var provider = ServiceProviderFactory.Create(workspace.Root);
        var settings = provider.GetRequiredService<IAppSettingsService>();
        await settings.AddRecentProjectAsync(new RecentProjectEntry
        {
            FilePath = workspace.FilePath("Алмаз.scri"),
            Name = "Алмазный трон",
            LastOpenedAt = DateTimeOffset.UtcNow,
        });
        await settings.AddRecentProjectAsync(new RecentProjectEntry
        {
            FilePath = workspace.FilePath("Тени.scri"),
            Name = "Тени города",
            LastOpenedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
        });

        var launcher = provider.GetRequiredService<LauncherViewModel>();
        await launcher.InitializeAsync();
        Assert.Equal(2, launcher.VisibleProjects.Count);

        launcher.ProjectSearchQuery = "тени";

        Assert.Single(launcher.VisibleProjects);
        Assert.Equal("Тени города", launcher.VisibleProjects[0].Name);

        // Пустой запрос (в том числе из одних пробелов) возвращает весь список.
        launcher.ProjectSearchQuery = "   ";
        Assert.Equal(2, launcher.VisibleProjects.Count);
    }

    [AvaloniaFact]
    public async Task Select_project_command_changes_context_selection()
    {
        using var workspace = new TempWorkspace();
        using var provider = ServiceProviderFactory.Create(workspace.Root);
        var settings = provider.GetRequiredService<IAppSettingsService>();
        await settings.AddRecentProjectAsync(new RecentProjectEntry
        {
            FilePath = workspace.FilePath("Первый.scri"),
            Name = "Первый",
            LastOpenedAt = DateTimeOffset.UtcNow,
        });
        await settings.AddRecentProjectAsync(new RecentProjectEntry
        {
            FilePath = workspace.FilePath("Второй.scri"),
            Name = "Второй",
            LastOpenedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
        });

        var launcher = provider.GetRequiredService<LauncherViewModel>();
        await launcher.InitializeAsync();
        var second = launcher.VisibleProjects.First(item => item.Name == "Второй");

        launcher.SelectProjectCommand.Execute(second);

        Assert.Same(second, launcher.SelectedProject);
        Assert.True(launcher.HasSelectedProject);
    }

    [AvaloniaFact]
    public async Task Remove_from_list_drops_entry_but_keeps_the_file()
    {
        using var workspace = new TempWorkspace();
        var projectPath = workspace.FilePath("Убрать.scri");
        var dialogs = new StubFileDialogService { CreatePath = projectPath };
        using var provider = ServiceProviderFactory.Create(workspace.Root, dialogs);
        var settings = provider.GetRequiredService<IAppSettingsService>();
        var launcher = provider.GetRequiredService<LauncherViewModel>();
        await launcher.InitializeAsync();
        await launcher.CreateProjectCommand.ExecuteAsync(null);

        var item = Assert.Single(launcher.RecentProjects);
        await launcher.RemoveRecentCommand.ExecuteAsync(item);

        Assert.True(File.Exists(projectPath));
        Assert.Empty(launcher.RecentProjects);
        Assert.Empty(launcher.VisibleProjects);
        Assert.DoesNotContain(settings.Current.RecentProjects, entry => entry.FilePath == projectPath);
    }

    [AvaloniaFact]
    public async Task Remove_selected_project_button_uses_the_selected_card()
    {
        using var workspace = new TempWorkspace();
        var projectPath = workspace.FilePath("Выбранный.scri");
        var dialogs = new StubFileDialogService { CreatePath = projectPath };
        using var provider = ServiceProviderFactory.Create(workspace.Root, dialogs);
        var launcher = provider.GetRequiredService<LauncherViewModel>();
        await launcher.InitializeAsync();
        await launcher.CreateProjectCommand.ExecuteAsync(null);

        Assert.NotNull(launcher.SelectedProject);
        await launcher.RemoveSelectedProjectCommand.ExecuteAsync(null);

        Assert.Empty(launcher.RecentProjects);
        Assert.True(File.Exists(projectPath));
    }

    [AvaloniaFact]
    public async Task Open_project_opens_the_first_scene_in_the_editor()
    {
        using var workspace = new TempWorkspace();
        var projectPath = workspace.FilePath("В редактор.scri");

        // Готовим проект со структурой на диске: книга → глава → сцена.
        using (var setup = ServiceProviderFactory.Create(workspace.Root))
        {
            var projects = setup.GetRequiredService<IProjectService>();
            var created = await projects.CreateAsync(new CreateProjectRequest
            {
                FilePath = projectPath,
                Name = "В редактор",
            });
            Assert.True(created.IsSuccess, created.Error?.TechnicalDetails);

            var architecture = setup.GetRequiredService<IBookArchitectureService>();
            var withBook = await architecture.CreateBookAsync(new CreateBookRequest
            {
                SeriesId = projects.Current!.Series[0].Id,
                Title = "Книга",
            });
            var bookId = withBook.Require().Series[0].Books[0].Id;
            await architecture.CreateChapterAsync(new CreateChapterRequest
            {
                BookId = bookId,
                Title = "Глава",
            });
            var chapterId = projects.Current!.Series[0].Books[0].Chapters[0].Id;
            await architecture.CreateSceneAsync(new CreateSceneRequest
            {
                ChapterId = chapterId,
                Title = "Сцена",
            });
            Assert.True((await projects.SaveAsync()).IsSuccess);
        }

        var dialogs = new StubFileDialogService { OpenPath = projectPath };
        using var provider = ServiceProviderFactory.Create(workspace.Root, dialogs);
        var launcher = provider.GetRequiredService<LauncherViewModel>();
        var navigation = provider.GetRequiredService<INavigationService>();
        await launcher.InitializeAsync();

        await launcher.OpenProjectCommand.ExecuteAsync(null);

        // «Открыть» ведёт в редактор, а не оставляет автора на лаунчере.
        Assert.Equal(AppScreen.Editor, navigation.Current);
    }

    [AvaloniaFact]
    public async Task Recent_projects_survive_a_restart()
    {
        using var workspace = new TempWorkspace();
        var projectPath = workspace.FilePath("Перезапуск.scri");
        var dialogs = new StubFileDialogService { CreatePath = projectPath };

        using (var first = ServiceProviderFactory.Create(workspace.Root, dialogs))
        {
            var launcher = first.GetRequiredService<LauncherViewModel>();
            await launcher.InitializeAsync();
            await launcher.CreateProjectCommand.ExecuteAsync(null);
            Assert.Single(launcher.RecentProjects);
        }

        // Новый контейнер читает тот же файл настроек: список последних проектов
        // обязан восстановиться — иначе лаунчер после перезапуска пуст.
        using var provider = ServiceProviderFactory.Create(workspace.Root, dialogs);
        var reloaded = provider.GetRequiredService<LauncherViewModel>();
        await reloaded.InitializeAsync();

        var item = Assert.Single(reloaded.RecentProjects);
        Assert.Equal("Перезапуск", item.Name);
        Assert.Equal(projectPath, item.FilePath);
    }

    /// <summary>
    /// Добавляет открытому проекту книгу, главы и сцены — так же, как это делает
    /// экран «Архитектура произведения», чтобы проверить живую сводку карточки.
    /// </summary>
    private static async Task AddStructureAsync(IServiceProvider provider, int chapterCount, int sceneCount)
    {
        var projects = provider.GetRequiredService<IProjectService>();
        var architecture = provider.GetRequiredService<IBookArchitectureService>();
        var project = projects.Current ?? throw new InvalidOperationException("Проект не открыт.");

        var withBook = await architecture.CreateBookAsync(new CreateBookRequest
        {
            SeriesId = project.Series[0].Id,
            Title = "Книга 1",
        });
        Assert.True(withBook.IsSuccess, withBook.Error?.TechnicalDetails);
        var bookId = withBook.Require().Series[0].Books[0].Id;

        for (var chapter = 0; chapter < chapterCount; chapter++)
        {
            var withChapter = await architecture.CreateChapterAsync(new CreateChapterRequest
            {
                BookId = bookId,
                Title = $"Глава {chapter + 1}",
            });
            Assert.True(withChapter.IsSuccess, withChapter.Error?.TechnicalDetails);
        }

        var chapterId = projects.Current!.Series[0].Books[0].Chapters[0].Id;
        for (var scene = 0; scene < sceneCount; scene++)
        {
            var withScene = await architecture.CreateSceneAsync(new CreateSceneRequest
            {
                ChapterId = chapterId,
                Title = $"Сцена {scene + 1}",
            });
            Assert.True(withScene.IsSuccess, withScene.Error?.TechnicalDetails);
        }
    }
}
