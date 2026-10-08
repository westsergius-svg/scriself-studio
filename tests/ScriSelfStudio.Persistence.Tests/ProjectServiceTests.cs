namespace ScriSelfStudio.Persistence.Tests;

using System.IO;
using ScriSelfStudio.Contracts.Errors;
using ScriSelfStudio.Contracts.Projects;
using ScriSelfStudio.Core.Events;
using ScriSelfStudio.Core;
using ScriSelfStudio.Core.Model;
using ScriSelfStudio.Persistence.FileSystem;
using ScriSelfStudio.Persistence.Projects;
using ScriSelfStudio.Persistence.Sqlite;
using ScriSelfStudio.TestSupport;

public sealed class ProjectServiceTests
{
    [Fact]
    public async Task Create_save_close_and_open_returns_same_project()
    {
        using var workspace = new TempWorkspace();
        var path = workspace.FilePath("Рукопись.scri");
        var events = new EventBus();
        var service = CreateService(events);

        var created = await service.CreateAsync(new CreateProjectRequest { FilePath = path, Name = "Рукопись" });
        Assert.True(created.IsSuccess, created.Error?.TechnicalDetails);
        Assert.True(File.Exists(path));
        Assert.Single(created.Require().Series);
        Assert.Equal(ProjectService.DefaultSeriesTitle, created.Require().Series[0].Title);

        var sceneId = AddScene(service, "Текст сцены");

        Assert.True(service.IsDirty);
        Assert.True((await service.SaveAsync()).IsSuccess);
        Assert.False(service.IsDirty);
        Assert.True((await service.CloseAsync(discardChanges: false)).IsSuccess);
        Assert.Null(service.Current);

        var opened = await service.OpenAsync(path);
        Assert.True(opened.IsSuccess, opened.Error?.TechnicalDetails);
        Assert.False(service.IsDirty);

        var document = service.GetSceneDocument(sceneId);
        Assert.True(document.IsSuccess);
        Assert.Equal("Текст сцены", document.Require().Document.ToPlainText());
    }

    [Fact]
    public async Task Create_requires_scr_extension_and_non_empty_name()
    {
        using var workspace = new TempWorkspace();
        var service = CreateService();

        var wrongExtension = await service.CreateAsync(new CreateProjectRequest
        {
            FilePath = workspace.FilePath("Проект.docx"),
            Name = "Проект",
        });
        var emptyName = await service.CreateAsync(new CreateProjectRequest
        {
            FilePath = workspace.FilePath("Проект.scri"),
            Name = "   ",
        });

        Assert.Equal(ErrorCode.ValidationFailed, wrongExtension.Error!.Code);
        Assert.Equal(ErrorCode.ValidationFailed, emptyName.Error!.Code);
    }

    [Fact]
    public async Task Path_without_extension_gets_scr_suffix()
    {
        using var workspace = new TempWorkspace();
        var service = CreateService();

        var created = await service.CreateAsync(new CreateProjectRequest
        {
            FilePath = workspace.FilePath("БезРасширения"),
            Name = "Проект",
        });

        Assert.True(created.IsSuccess);
        Assert.EndsWith(".scri", service.CurrentFilePath, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(service.CurrentFilePath!));
    }

    [Fact]
    public async Task Second_project_cannot_be_opened_without_closing_current()
    {
        using var workspace = new TempWorkspace();
        var service = CreateService();
        var path = workspace.FilePath("Первый.scri");
        await service.CreateAsync(new CreateProjectRequest { FilePath = path, Name = "Первый" });

        var second = await service.CreateAsync(new CreateProjectRequest
        {
            FilePath = workspace.FilePath("Второй.scri"),
            Name = "Второй",
        });

        Assert.False(second.IsSuccess);
        Assert.Equal(ErrorCode.ProjectAlreadyOpen, second.Error!.Code);
    }

    [Fact]
    public async Task Close_refuses_to_discard_dirty_project_without_confirmation()
    {
        using var workspace = new TempWorkspace();
        var service = CreateService();
        await service.CreateAsync(new CreateProjectRequest
        {
            FilePath = workspace.FilePath("Проект.scri"),
            Name = "Проект",
        });

        service.Mutate(project =>
        {
            project.Name = "Изменён";
            return Result.Success;
        });

        var refused = await service.CloseAsync(discardChanges: false);
        Assert.False(refused.IsSuccess);
        Assert.Equal(ErrorCode.ValidationFailed, refused.Error!.Code);
        Assert.NotNull(service.Current);

        Assert.True((await service.CloseAsync(discardChanges: true)).IsSuccess);
    }

    [Fact]
    public async Task Save_removes_recovery_snapshot_after_success()
    {
        using var workspace = new TempWorkspace();
        var path = workspace.FilePath("Проект.scri");
        var recovery = new FileProjectRecoveryStore();
        var service = CreateService(recovery: recovery);
        await service.CreateAsync(new CreateProjectRequest { FilePath = path, Name = "Проект" });
        service.Mutate(project =>
        {
            project.Name = "Изменён";
            return Result.Success;
        });

        Assert.True((await service.SaveAsync()).IsSuccess);

        Assert.False(File.Exists(recovery.GetRecoveryPath(path)));
        Assert.Null((await service.DetectRecoveryAsync(path)).Require());
    }

    [Fact]
    public async Task Recovery_snapshot_is_detected_and_restored_as_dirty_project()
    {
        using var workspace = new TempWorkspace();
        var path = workspace.FilePath("Проект.scri");
        var recovery = new FileProjectRecoveryStore();
        var service = CreateService(recovery: recovery);
        await service.CreateAsync(new CreateProjectRequest { FilePath = path, Name = "Проект" });
        Assert.True((await service.CloseAsync(discardChanges: false)).IsSuccess);

        var project = SampleProjects.CreateBookWithScenes();
        await recovery.WriteAsync(project, path);

        var detected = (await service.DetectRecoveryAsync(path)).Require();
        Assert.NotNull(detected);
        Assert.Equal(project.Id, detected!.ProjectId);

        var restored = await service.RecoverAsync(detected);
        Assert.True(restored.IsSuccess, restored.Error?.TechnicalDetails);
        Assert.True(service.IsDirty);
        Assert.Equal(project.Id, restored.Require().Id);

        Assert.True((await service.DiscardRecoveryAsync(detected)).IsSuccess);
        Assert.Null((await service.DetectRecoveryAsync(path)).Require());
    }

    [Fact]
    public async Task Scene_document_update_increments_revision_and_detects_conflict()
    {
        using var workspace = new TempWorkspace();
        var service = CreateService();
        await service.CreateAsync(new CreateProjectRequest
        {
            FilePath = workspace.FilePath("Проект.scri"),
            Name = "Проект",
        });

        var sceneId = AddScene(service);

        var document = service.GetSceneDocument(sceneId);
        Assert.True(document.IsSuccess);
        Assert.Equal(0, document.Require().Revision);

        var updated = await service.UpdateSceneDocumentAsync(new UpdateSceneDocumentRequest
        {
            SceneId = sceneId,
            BaseRevision = 0,
            Document = SampleProjects.DocumentWithText("Новый текст"),
        });

        Assert.True(updated.IsSuccess);
        Assert.Equal(1, updated.Require().Revision);
        Assert.Equal("Новый текст", service.GetSceneDocument(sceneId).Require().Document.ToPlainText());

        var conflict = await service.UpdateSceneDocumentAsync(new UpdateSceneDocumentRequest
        {
            SceneId = sceneId,
            BaseRevision = 0,
            Document = SampleProjects.DocumentWithText("Устаревшая правка"),
        });

        Assert.False(conflict.IsSuccess);
        Assert.Equal(ErrorCode.EditorOperationConflict, conflict.Error!.Code);
        Assert.Equal("Новый текст", service.GetSceneDocument(sceneId).Require().Document.ToPlainText());
    }

    [Fact]
    public async Task Get_scene_document_returns_copy_not_live_document()
    {
        using var workspace = new TempWorkspace();
        var service = CreateService();
        await service.CreateAsync(new CreateProjectRequest
        {
            FilePath = workspace.FilePath("Проект.scri"),
            Name = "Проект",
        });

        var sceneId = AddScene(service, "Канонический текст");

        var document = service.GetSceneDocument(sceneId).Require().Document;
        document.Blocks[0].Spans[0].Text = "Подмена";

        Assert.Equal("Канонический текст", service.GetSceneDocument(sceneId).Require().Document.ToPlainText());
    }

    [Fact]
    public async Task Save_without_open_project_reports_error()
    {
        var service = CreateService();

        var save = await service.SaveAsync();
        var close = await service.CloseAsync(discardChanges: true);

        Assert.Equal(ErrorCode.ProjectNotOpen, save.Error!.Code);
        Assert.Equal(ErrorCode.ProjectNotOpen, close.Error!.Code);
    }

    /// <summary>
    /// Поля макета линии (цель, статус, заметки) и значок шага обязаны переживать
    /// запись в SQLite и обратное чтение: иначе экран линий теряет данные автора.
    /// </summary>
    [Fact]
    public async Task Plot_line_goal_status_notes_and_step_icon_survive_sqlite_round_trip()
    {
        using var workspace = new TempWorkspace();
        var path = workspace.FilePath("Рукопись.scri");
        var service = CreateService();
        var created = await service.CreateAsync(new CreateProjectRequest { FilePath = path, Name = "Рукопись" });
        Assert.True(created.IsSuccess, created.Error?.TechnicalDetails);

        var mutated = service.Mutate(project =>
        {
            var line = ProjectFactory.CreatePlotLine(project, "Главная сюжетная линия");
            line.Description = "Поиск источника магии";
            line.Goal = "Найти источник магии";
            line.Status = PlotLineStatus.Done;
            line.Notes = "Держать в тайне до третьей главы";

            var step = ProjectFactory.CreatePlotLineStep(line, "Начало", 0);
            step.Icon = "start";
            line.Steps.Add(step);
            project.PlotLines.Add(line);
            return Result.Success;
        });
        Assert.True(mutated.IsSuccess, mutated.Error?.TechnicalDetails);
        Assert.True((await service.SaveAsync()).IsSuccess);
        Assert.True((await service.CloseAsync(discardChanges: false)).IsSuccess);

        var opened = await service.OpenAsync(path);
        Assert.True(opened.IsSuccess, opened.Error?.TechnicalDetails);

        var lineDto = Assert.Single(opened.Require().PlotLines);
        Assert.Equal("Поиск источника магии", lineDto.Description);
        Assert.Equal("Найти источник магии", lineDto.Goal);
        Assert.Equal(PlotLineStatus.Done, lineDto.Status);
        Assert.Equal("Держать в тайне до третьей главы", lineDto.Notes);
        Assert.Equal("start", Assert.Single(lineDto.Steps).Icon);
    }

    private static ProjectService CreateService(
        Core.Events.IEventBus? events = null,
        FileProjectRecoveryStore? recovery = null) =>
        new(
            new SqliteProjectStore(),
            recovery ?? (Contracts.Storage.IProjectRecoveryStore)new FileProjectRecoveryStore(),
            events ?? new EventBus(),
            new FakeClock());

    /// <summary>Добавляет в открытый проект книгу, главу и сцену, возвращая идентификатор сцены.</summary>
    private static Guid AddScene(ProjectService service, string? text = null)
    {
        var sceneId = Guid.Empty;
        var mutated = service.Mutate(project =>
        {
            var series = project.Series[0];
            var book = ProjectFactory.CreateBook(project, series, "Книга");
            series.Books.Add(book);

            var chapter = ProjectFactory.CreateChapter(book, "Глава");
            book.Chapters.Add(chapter);

            var scene = ProjectFactory.CreateScene(chapter, "Сцена");
            if (text is not null)
            {
                scene.Document = SampleProjects.DocumentWithText(text);
            }

            chapter.Scenes.Add(scene);
            sceneId = scene.Id;
            return Result.Success;
        });

        Assert.True(mutated.IsSuccess, mutated.Error?.TechnicalDetails);
        return sceneId;
    }
}
