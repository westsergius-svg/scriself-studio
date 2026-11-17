namespace ScriSelfStudio.Persistence.Projects;

using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ScriSelfStudio.Contracts.Dtos;
using ScriSelfStudio.Contracts.Errors;
using ScriSelfStudio.Contracts.Events;
using ScriSelfStudio.Contracts.Projects;
using ScriSelfStudio.Contracts.Storage;
using ScriSelfStudio.Core;
using ScriSelfStudio.Core.Events;
using ScriSelfStudio.Core.Json;
using ScriSelfStudio.Core.Model;
using ScriSelfStudio.Core.Text;
using ScriSelfStudio.Persistence.FileSystem;
using ScriSelfStudio.Persistence.Sqlite;

/// <summary>
/// Владелец открытого проекта (ТЗ п. 7, 22, 23): создание, открытие, сохранение,
/// восстановление и выдача снимков для UI.
/// </summary>
public sealed partial class ProjectService : IProjectService, IDisposable
{
    public const string DefaultSeriesTitle = "Основная";

    private readonly IProjectStore _store;
    private readonly IProjectRecoveryStore _recovery;
    private readonly IEventBus _events;
    private readonly ISystemClock _clock;
    private readonly ILogger<ProjectService>? _logger;

    private Project? _project;
    private string? _filePath;
    private bool _isDirty;
    private long _revision;
    private ProjectFileVersion? _fileVersion;
    private readonly SemaphoreSlim _operationGate = new(1, 1);

    public ProjectService(
        IProjectStore store,
        IProjectRecoveryStore recovery,
        IEventBus events,
        ISystemClock? clock = null,
        ILogger<ProjectService>? logger = null)
    {
        _store = store;
        _recovery = recovery;
        _events = events;
        _clock = clock ?? new SystemClock();
        _logger = logger;
    }

    public ProjectDto? Current => _project is null || _filePath is null
        ? null
        : _project.ToDto(_filePath, _isDirty);

    public string? CurrentFilePath => _filePath;

    public string? CurrentSourceFormat => _project?.SourceFormat;

    public bool IsDirty => _isDirty;

    public Task<Result<ProjectDto>> CreateAsync(CreateProjectRequest request, CancellationToken cancellationToken = default) =>
        RunSerializedAsync(() => CreateCoreAsync(request, cancellationToken), cancellationToken);

    private async Task<Result<ProjectDto>> CreateCoreAsync(CreateProjectRequest request, CancellationToken cancellationToken)
    {
        if (_project is not null)
        {
            return Result<ProjectDto>.FromError(
                ErrorCode.ProjectAlreadyOpen,
                "Сначала закройте текущий проект.",
                "Закройте текущий проект и повторите создание.");
        }

        var pathResult = NormalizeProjectPath(request.FilePath);
        if (!pathResult.IsSuccess)
        {
            return Result<ProjectDto>.Failure(pathResult.Error!);
        }

        var nameResult = ValidateProjectName(request.Name);
        if (!nameResult.IsSuccess)
        {
            return Result<ProjectDto>.Failure(nameResult.Error!);
        }

        var now = _clock.UtcNow;
        var project = ProjectFactory.CreateProject(nameResult.Require(), request.Description, now);
        project.Series.Add(ProjectFactory.CreateSeries(project, DefaultSeriesTitle, now));

        var path = pathResult.Require();
        var saved = await SaveCoreAsync(project, path, createBackup: false, cancellationToken);
        if (!saved.IsSuccess)
        {
            return Result<ProjectDto>.Failure(saved.Error!);
        }

        await _recovery.DeleteAsync(path, cancellationToken);
        SetOpenProject(project, path, isDirty: false);
        _events.Publish(new ProjectOpened(project.Id, path, project.Name));
        return Result<ProjectDto>.Success(Current!);
    }

    public Task<Result<ProjectDto>> OpenAsync(string filePath, CancellationToken cancellationToken = default) =>
        RunSerializedAsync(() => OpenCoreAsync(filePath, cancellationToken), cancellationToken);

    private async Task<Result<ProjectDto>> OpenCoreAsync(string filePath, CancellationToken cancellationToken)
    {
        if (_project is not null)
        {
            return Result<ProjectDto>.FromError(
                ErrorCode.ProjectAlreadyOpen,
                "Сначала закройте текущий проект.",
                "Закройте текущий проект и повторите открытие.");
        }

        var pathResult = NormalizeProjectPath(filePath);
        if (!pathResult.IsSuccess)
        {
            return Result<ProjectDto>.Failure(pathResult.Error!);
        }

        var path = pathResult.Require();
        var acquired = ProjectFileAccess.AcquireLease(path);
        if (!acquired.IsSuccess) return Result<ProjectDto>.Failure(acquired.Error!);
        using var lease = acquired.Require();
        var loaded = await _store.LoadAsync(path, cancellationToken);
        if (!loaded.IsSuccess)
        {
            return Result<ProjectDto>.Failure(loaded.Error!);
        }

        var loadResult = loaded.Require();
        var project = loadResult.Project;
        RefreshSceneWords(project);
        try { _fileVersion = ProjectFileAccess.ReadVersion(path); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return Result<ProjectDto>.FromError(ErrorCode.StorageReadFailed,
                "Не удалось проверить файл проекта. Повторите открытие.", technicalDetails: error.Message, exception: error);
        }
        SetOpenProject(project, path, isDirty: false);
        _events.Publish(new ProjectOpened(project.Id, path, project.Name));

        // Предупреждения импорта показываем пользователю: без них неизвестно, что
        // часть содержимого была восстановлена приблизительно.
        if (loadResult.Warnings.Count > 0)
        {
            _events.Publish(new ProjectImported(project.Id, project.SourceFormat ?? "unknown", loadResult.Warnings));
        }

        return Result<ProjectDto>.Success(Current!);
    }

    public Task<Result> SaveAsync(CancellationToken cancellationToken = default) =>
        RunSerializedAsync(() => SaveCurrentCoreAsync(cancellationToken), cancellationToken);

    private async Task<Result> SaveCurrentCoreAsync(CancellationToken cancellationToken)
    {
        if (_project is null || _filePath is null)
        {
            return Result.FromError(ErrorCode.ProjectNotOpen);
        }

        // Проект в чужом формате перезаписывается только с резервной копией (ТЗ п. 13).
        var createBackup = _project.Settings.BackupsEnabled || _project.SourceFormat is not null;
        return await SaveCoreAsync(_project, _filePath, createBackup, cancellationToken);
    }

    public Task<Result> SaveAsAsync(string filePath, CancellationToken cancellationToken = default) =>
        RunSerializedAsync(() => SaveAsCoreAsync(filePath, cancellationToken), cancellationToken);

    private async Task<Result> SaveAsCoreAsync(string filePath, CancellationToken cancellationToken)
    {
        if (_project is null)
        {
            return Result.FromError(ErrorCode.ProjectNotOpen);
        }

        var pathResult = NormalizeProjectPath(filePath);
        if (!pathResult.IsSuccess)
        {
            return Result.Failure(pathResult.Error!);
        }

        var path = pathResult.Require();
        var createBackup = _project.Settings.BackupsEnabled || _project.SourceFormat is not null;
        var saved = await SaveCoreAsync(_project, path, createBackup, cancellationToken);
        if (!saved.IsSuccess)
        {
            return saved;
        }

        _filePath = path;
        _events.Publish(new ProjectChanged(_project.Id, _isDirty));
        return Result.Success;
    }

    public Task<Result> CloseAsync(bool discardChanges, CancellationToken cancellationToken = default) =>
        RunSerializedAsync(() => CloseCoreAsync(discardChanges), cancellationToken);

    private Task<Result> CloseCoreAsync(bool discardChanges)
    {
        if (_project is null)
        {
            return Task.FromResult(Result.FromError(ErrorCode.ProjectNotOpen));
        }

        if (_isDirty && !discardChanges)
        {
            return Task.FromResult(Result.FromError(
                ErrorCode.ValidationFailed,
                "В проекте есть несохранённые изменения.",
                "Сохраните проект или подтвердите закрытие без сохранения."));
        }

        var projectId = _project.Id;
        _project = null;
        _filePath = null;
        _isDirty = false;
        _fileVersion = null;
        _revision = 0;
        _events.Publish(new ProjectClosed(projectId));
        return Task.FromResult(Result.Success);
    }

    public Result<ProjectDto> Mutate(Func<Project, Result> change)
    {
        if (_project is null)
        {
            return Result<ProjectDto>.FromError(ErrorCode.ProjectNotOpen);
        }

        var result = change(_project);
        if (!result.IsSuccess)
        {
            return Result<ProjectDto>.Failure(result.Error!);
        }

        _project.Audit.UpdatedAt = _clock.UtcNow;
        var wasDirty = _isDirty;
        SetDirty(true);

        // SetDirty публикует событие только при смене флага, но структура проекта
        // (книга, глава, сцена, персонаж) уже изменилась. Подписчики — лаунчер с
        // карточкой проекта и контекстные панели — должны увидеть свежую сводку,
        // поэтому при повторной правке публикуем событие явно.
        if (wasDirty)
        {
            _events.Publish(new ProjectChanged(_project.Id, _isDirty));
        }

        return Result<ProjectDto>.Success(Current!);
    }

    public Result<SceneDocumentDto> GetSceneDocument(Guid sceneId)
    {
        if (_project is null)
        {
            return Result<SceneDocumentDto>.FromError(ErrorCode.ProjectNotOpen);
        }

        var scene = _project.FindScene(sceneId);
        if (scene is null)
        {
            return Result<SceneDocumentDto>.FromError(ErrorCode.EntityNotFound, "Сцена не найдена.");
        }

        return Result<SceneDocumentDto>.Success(new SceneDocumentDto
        {
            SceneId = scene.Id,
            Revision = scene.Revision,
            Document = scene.Document.Clone(),
        });
    }

    public Task<Result<SceneDocumentDto>> UpdateSceneDocumentAsync(UpdateSceneDocumentRequest request, CancellationToken cancellationToken = default)
    {
        if (_project is null)
        {
            return Task.FromResult(Result<SceneDocumentDto>.FromError(ErrorCode.ProjectNotOpen));
        }

        var scene = _project.FindScene(request.SceneId);
        if (scene is null)
        {
            return Task.FromResult(Result<SceneDocumentDto>.FromError(ErrorCode.EntityNotFound, "Сцена не найдена."));
        }

        if (scene.Revision != request.BaseRevision)
        {
            return Task.FromResult(Result<SceneDocumentDto>.FromError(
                ErrorCode.EditorOperationConflict,
                "Текст сцены был изменён после открытия.",
                "Откройте сцену заново."));
        }

        try { ReviewEditing.Validate(request.Document); }
        catch (InvalidDataException error)
        {
            return Task.FromResult(Result<SceneDocumentDto>.FromError(ErrorCode.ValidationFailed,
                "Данные редактуры повреждены. Изменения не применены.", technicalDetails: error.Message));
        }
        scene.Document = request.Document.Clone();
        scene.Words = WordCounting.Count(scene.Document);
        scene.Revision++;
        scene.Touch(_clock.UtcNow);
        _project.Audit.UpdatedAt = _clock.UtcNow;
        SetDirty(true);
        _events.Publish(new SceneChanged(scene.Id, scene.Revision));

        return Task.FromResult(Result<SceneDocumentDto>.Success(new SceneDocumentDto
        {
            SceneId = scene.Id,
            Revision = scene.Revision,
            Document = scene.Document.Clone(),
        }));
    }

    public Task<Result<ProjectDto>> UpdateSceneAsync(UpdateSceneRequest request, CancellationToken cancellationToken = default)
    {
        if (_project is null)
        {
            return Task.FromResult(Result<ProjectDto>.FromError(ErrorCode.ProjectNotOpen));
        }

        var scene = _project.FindScene(request.SceneId);
        if (scene is null)
        {
            return Task.FromResult(Result<ProjectDto>.FromError(ErrorCode.EntityNotFound, "Сцена не найдена."));
        }

        if (request.Title is not null)
        {
            var title = request.Title.Trim();
            if (title.Length == 0)
            {
                return Task.FromResult(Result<ProjectDto>.FromError(
                    ErrorCode.ValidationFailed,
                    "Название сцены не может быть пустым."));
            }

            scene.Title = title;
        }

        if (request.Description is not null)
        {
            scene.Description = request.Description;
        }

        if (request.Status is not null)
        {
            scene.Status = request.Status.Value;
        }

        if (request.IsKey is { } isKey)
        {
            scene.Metadata.IsKey = isKey;
        }

        if (request.IsLocked is { } isLocked)
        {
            scene.Metadata.IsLocked = isLocked;
        }

        if (request.Notes is not null)
        {
            scene.Metadata.Notes = request.Notes;
        }

        if (request.PlotLineStepIds is { } plotLineStepIds)
        {
            var applied = ApplyPlotLineSteps(_project, scene, plotLineStepIds);
            if (!applied.IsSuccess)
            {
                return Task.FromResult(Result<ProjectDto>.Failure(applied.Error!));
            }
        }

        scene.Touch(_clock.UtcNow);
        _project.Audit.UpdatedAt = _clock.UtcNow;
        SetDirty(true);
        _events.Publish(new SceneChanged(scene.Id, scene.Revision));
        return Task.FromResult(Result<ProjectDto>.Success(Current!));
    }

    /// <summary>
    /// Приводит привязку сцены к этапам линий в согласованное состояние (ADR 012):
    /// этапы из запроса получают сцену, остальные её теряют. Связь задаётся со стороны
    /// сцены, поэтому у этапа нет отдельной операции «привязать сцену».
    /// </summary>
    private static Result ApplyPlotLineSteps(Project project, Scene scene, IReadOnlyList<Guid> stepIds)
    {
        var desired = new HashSet<Guid>(stepIds);
        var known = project.PlotLines
            .SelectMany(line => line.Steps)
            .Where(step => !step.IsDeleted)
            .Select(step => step.Id)
            .ToHashSet();

        if (!desired.IsSubsetOf(known))
        {
            return Result.FromError(ErrorCode.EntityNotFound, "Шаг сюжетной линии не найден.");
        }

        scene.Metadata.PlotLineStepOrder.Clear();
        scene.Metadata.PlotLineStepOrder.AddRange(stepIds.Distinct());

        foreach (var step in project.PlotLines.SelectMany(line => line.Steps))
        {
            if (desired.Contains(step.Id))
            {
                if (!step.SceneIds.Contains(scene.Id))
                {
                    step.SceneIds.Add(scene.Id);
                }
            }
            else
            {
                step.SceneIds.Remove(scene.Id);
            }
        }

        return Result.Success;
    }

    public async Task<Result<ProjectDto>> UpdateSettingsAsync(UpdateProjectSettingsRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_project is null || _filePath is null)
        {
            return Result<ProjectDto>.FromError(ErrorCode.ProjectNotOpen);
        }

        var validation = ValidateSettings(request);
        if (!validation.IsSuccess)
        {
            return Result<ProjectDto>.Failure(validation.Error!);
        }

        var settings = _project.Settings;
        settings.Theme = request.Theme;
        settings.FontFamily = request.FontFamily.Trim();
        settings.FontSize = request.FontSize;
        settings.AutoSaveEnabled = request.AutoSaveEnabled;
        settings.AutoSaveIntervalSeconds = request.AutoSaveIntervalSeconds;
        settings.BackupsEnabled = request.BackupsEnabled;
        settings.Language = request.Language.Trim();

        // Настройки — явное действие пользователя: сохраняем сразу, чтобы закрытие
        // проекта без ручного сохранения их не потеряло (ТЗ п. 27, 36).
        var saved = await SaveCoreAsync(_project, _filePath, settings.BackupsEnabled, cancellationToken);
        if (!saved.IsSuccess)
        {
            return Result<ProjectDto>.Failure(saved.Error!);
        }

        return Result<ProjectDto>.Success(Current!);
    }

    private static Result ValidateSettings(UpdateProjectSettingsRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.FontFamily))
        {
            return Result.FromError(
                ErrorCode.ValidationFailed,
                "Укажите название шрифта.",
                "Впишите имя установленного шрифта, например Inter.");
        }

        if (request.FontSize < ProjectSettings.MinFontSize || request.FontSize > ProjectSettings.MaxFontSize)
        {
            return Result.FromError(
                ErrorCode.ValidationFailed,
                $"Размер шрифта должен быть от {ProjectSettings.MinFontSize:0} до {ProjectSettings.MaxFontSize:0}.");
        }

        if (request.AutoSaveIntervalSeconds < ProjectSettings.MinAutoSaveIntervalSeconds ||
            request.AutoSaveIntervalSeconds > ProjectSettings.MaxAutoSaveIntervalSeconds)
        {
            return Result.FromError(
                ErrorCode.ValidationFailed,
                $"Интервал автосохранения должен быть от {ProjectSettings.MinAutoSaveIntervalSeconds} до {ProjectSettings.MaxAutoSaveIntervalSeconds} секунд.");
        }

        if (string.IsNullOrWhiteSpace(request.Language))
        {
            return Result.FromError(ErrorCode.ValidationFailed, "Укажите язык приложения.");
        }

        return Result.Success;
    }

    public async Task<Result<RecoveryInfo?>> DetectRecoveryAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var pathResult = NormalizeProjectPath(filePath);
        if (!pathResult.IsSuccess)
        {
            return Result<RecoveryInfo?>.Failure(pathResult.Error!);
        }

        return await _recovery.FindAsync(pathResult.Require(), cancellationToken);
    }

    public Task<Result<ProjectDto>> RecoverAsync(RecoveryInfo recovery, CancellationToken cancellationToken = default) =>
        RunSerializedAsync(() => RecoverCoreAsync(recovery, cancellationToken), cancellationToken);

    private async Task<Result<ProjectDto>> RecoverCoreAsync(RecoveryInfo recovery, CancellationToken cancellationToken)
    {
        if (_project is not null)
        {
            return Result<ProjectDto>.FromError(
                ErrorCode.ProjectAlreadyOpen,
                "Сначала закройте текущий проект.",
                "Закройте текущий проект и повторите восстановление.");
        }

        var read = await _recovery.ReadAsync(recovery, cancellationToken);
        if (!read.IsSuccess)
        {
            return Result<ProjectDto>.Failure(read.Error!);
        }

        var recovered = read.Require();
        RefreshSceneWords(recovered);
        try { _fileVersion = ProjectFileAccess.ReadVersion(recovery.ProjectFilePath); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return Result<ProjectDto>.FromError(ErrorCode.StorageReadFailed,
                "Не удалось проверить исходный файл проекта. Повторите восстановление.", technicalDetails: error.Message, exception: error);
        }
        SetOpenProject(recovered, recovery.ProjectFilePath, isDirty: true);
        _events.Publish(new ProjectOpened(recovery.ProjectId, recovery.ProjectFilePath, recovery.ProjectName));
        _events.Publish(new RecoveryDetected(recovery.RecoveryFilePath, recovery.WrittenAt));
        return Result<ProjectDto>.Success(Current!);
    }

    public async Task<Result> DiscardRecoveryAsync(RecoveryInfo recovery, CancellationToken cancellationToken = default) =>
        await _recovery.DeleteAsync(recovery.ProjectFilePath, cancellationToken);

    private async Task<Result> SaveCoreAsync(Project project, string filePath, bool createBackup, CancellationToken cancellationToken)
    {
        _events.Publish(new SaveStarted(project.Id, filePath));

        var acquired = ProjectFileAccess.AcquireLease(filePath);
        if (!acquired.IsSuccess)
        {
            _events.Publish(new SaveFailed(project.Id, filePath, acquired.Error!));
            return Result.Failure(acquired.Error!);
        }
        using var lease = acquired.Require();

        ProjectFileVersion expected;
        Project snapshot;
        var savedRevision = _revision;
        try
        {
            expected = ReferenceEquals(project, _project)
                ? string.Equals(filePath, _filePath, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
                    ? _fileVersion ?? ProjectFileAccess.ReadVersion(filePath)
                    : ProjectFileAccess.ReadVersion(filePath)
                : new ProjectFileVersion(false, null);
            snapshot = CloneProject(project);
            snapshot.Audit.UpdatedAt = _clock.UtcNow;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            var failure = Result.FromError(ErrorCode.StorageWriteFailed,
                "Не удалось подготовить сохранение проекта. Правки остались открыты.", technicalDetails: error.Message, exception: error);
            _events.Publish(new SaveFailed(project.Id, filePath, failure.Error!));
            return failure;
        }

        // Устаревший владелец не должен заменять ни книгу, ни recovery актуального владельца.
        var unchangedFile = ProjectFileAccess.CheckVersion(filePath, expected);
        if (!unchangedFile.IsSuccess)
        {
            _events.Publish(new SaveFailed(project.Id, filePath, unchangedFile.Error!));
            return unchangedFile;
        }

        // Снимок восстановления создаётся до записи: если приложение упадёт во время
        // сохранения, состояние не потеряется (ТЗ п. 13, 28).
        var recovery = await _recovery.WriteAsync(snapshot, filePath, cancellationToken);
        if (!recovery.IsSuccess)
        {
            _logger?.LogWarning("Не удалось создать снимок восстановления: {Code}.", recovery.Error!.Code);
        }

        var request = new ProjectSaveRequest
        {
            Project = snapshot,
            FilePath = filePath,
            CreateBackup = createBackup,
            ExpectedFileVersion = expected,
        };

        var saved = await _store.SaveAsync(request, cancellationToken);
        if (!saved.IsSuccess)
        {
            _events.Publish(new SaveFailed(project.Id, filePath, saved.Error!));
            return saved;
        }

        try { _fileVersion = ProjectFileAccess.ReadVersion(filePath); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            var failure = Result.FromError(ErrorCode.StorageReadFailed,
                "Книга записана, но проверить файл не удалось. Правки остались открыты; повторите сохранение или сохраните копию.",
                technicalDetails: error.Message, exception: error);
            _events.Publish(new SaveFailed(project.Id, filePath, failure.Error!));
            return failure;
        }

        // После записи отмена уже не должна прерывать защиту более новых правок.
        if (!ReferenceEquals(project, _project))
        {
            await _recovery.DeleteAsync(filePath, CancellationToken.None);
        }
        else
        {
            if (_revision == savedRevision)
            {
                await _recovery.DeleteAsync(filePath, CancellationToken.None);
            }

            if (_revision == savedRevision)
            {
                SetDirty(false);
            }
            else
            {
                await RefreshRecoveryAsync(project, filePath);
            }
        }
        _events.Publish(new SaveCompleted(project.Id, filePath, _clock.UtcNow));
        return Result.Success;
    }

    private async Task RefreshRecoveryAsync(Project project, string filePath)
    {
        long revision;
        do
        {
            revision = _revision;
            var result = await _recovery.WriteAsync(CloneProject(project), filePath, CancellationToken.None);
            if (!result.IsSuccess)
            {
                _logger?.LogWarning("Не удалось обновить снимок восстановления: {Code}.", result.Error!.Code);
                return;
            }
        }
        while (_revision != revision);
    }

    private static Project CloneProject(Project project) =>
        JsonSerializer.SerializeToElement(project, CoreJsonContext.Default.Project)
            .Deserialize(CoreJsonContext.Default.Project)!;

    private async Task<Result> RunSerializedAsync(Func<Task<Result>> operation, CancellationToken cancellationToken)
    {
        await _operationGate.WaitAsync(cancellationToken);
        try { return await operation(); }
        finally { _operationGate.Release(); }
    }

    private async Task<Result<T>> RunSerializedAsync<T>(Func<Task<Result<T>>> operation, CancellationToken cancellationToken)
    {
        await _operationGate.WaitAsync(cancellationToken);
        try { return await operation(); }
        finally { _operationGate.Release(); }
    }

    public void Dispose() => _operationGate.Dispose();

    private void SetOpenProject(Project project, string filePath, bool isDirty)
    {
        _project = project;
        _filePath = filePath;
        _isDirty = isDirty;
        _revision = 0;
    }

    /// <summary>
    /// Пересчитать объём сцен в словах при загрузке проекта: документы уже прочитаны,
    /// поэтому карта книги получает числа без отдельного чтения текстов (ТЗ п. 27).
    /// </summary>
    private static void RefreshSceneWords(Project project)
    {
        foreach (var scene in project.EnumerateSeries()
                     .SelectMany(series => series.Books)
                     .SelectMany(book => book.Chapters)
                     .SelectMany(chapter => chapter.Scenes))
        {
            scene.Words = WordCounting.Count(scene.Document);
        }
    }

    private void SetDirty(bool isDirty)
    {
        if (isDirty) _revision++;
        if (_isDirty == isDirty || _project is null)
        {
            _isDirty = isDirty;
            return;
        }

        _isDirty = isDirty;
        _events.Publish(new ProjectChanged(_project.Id, isDirty));
    }

    private static Result<string> ValidateProjectName(string? name)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return Result<string>.FromError(ErrorCode.ValidationFailed, "Укажите название проекта.");
        }

        if (trimmed.Length > 200)
        {
            return Result<string>.FromError(ErrorCode.ValidationFailed, "Название проекта длиннее 200 символов.");
        }

        return Result<string>.Success(trimmed);
    }

    private static Result<string> NormalizeProjectPath(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return Result<string>.FromError(ErrorCode.ValidationFailed, "Укажите путь к файлу проекта.");
        }

        var candidate = filePath.Trim();
        var extension = Path.GetExtension(candidate);
        if (extension.Length == 0)
        {
            candidate += SqliteConnectionFactory.FileExtension;
        }
        else if (!string.Equals(extension, SqliteConnectionFactory.FileExtension, StringComparison.OrdinalIgnoreCase))
        {
            return Result<string>.FromError(
                ErrorCode.ValidationFailed,
                $"Файл проекта должен иметь расширение {SqliteConnectionFactory.FileExtension}.");
        }

        try
        {
            return Result<string>.Success(Path.GetFullPath(candidate));
        }
        catch (ArgumentException exception)
        {
            return Result<string>.FromError(
                ErrorCode.ValidationFailed,
                "Путь к файлу проекта содержит недопустимые символы.",
                technicalDetails: exception.Message,
                exception: exception);
        }
        catch (PathTooLongException exception)
        {
            return Result<string>.FromError(
                ErrorCode.ValidationFailed,
                "Путь к файлу проекта слишком длинный.",
                technicalDetails: exception.Message,
                exception: exception);
        }
        catch (NotSupportedException exception)
        {
            return Result<string>.FromError(
                ErrorCode.ValidationFailed,
                "Путь к файлу проекта имеет неподдерживаемый формат.",
                technicalDetails: exception.Message,
                exception: exception);
        }
    }
}
