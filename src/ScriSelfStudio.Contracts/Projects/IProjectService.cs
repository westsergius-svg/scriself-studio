namespace ScriSelfStudio.Contracts.Projects;

using ScriSelfStudio.Contracts.Dtos;
using ScriSelfStudio.Contracts.Errors;
using ScriSelfStudio.Core.Model;
using ScriSelfStudio.Core.Text;

/// <summary>Параметры создания проекта (ТЗ п. 7, 28).</summary>
public sealed record CreateProjectRequest
{
    public required string FilePath { get; init; }

    public required string Name { get; init; }

    public string? Description { get; init; }
}

/// <summary>Текст сцены вместе с её ревизией.</summary>
public sealed record SceneDocumentDto
{
    public required Guid SceneId { get; init; }

    public required long Revision { get; init; }

    public required RichDocument Document { get; init; }
}

/// <summary>Запрос на замену текста сцены с защитой от потери чужих правок.</summary>
public sealed record UpdateSceneDocumentRequest
{
    public required Guid SceneId { get; init; }

    /// <summary>Ревизия, от которой работал редактор.</summary>
    public required long BaseRevision { get; init; }

    public required RichDocument Document { get; init; }
}

/// <summary>Запрос изменения свойств сцены (описание, статус, метаданные).</summary>
public sealed record UpdateSceneRequest
{
    public required Guid SceneId { get; init; }

    public string? Title { get; init; }

    public string? Description { get; init; }

    public ScriSelfStudio.Core.Model.SceneStatus? Status { get; init; }

    /// <summary>Ключевая сцена книги: <c>null</c> — не менять (ТЗ п. 27).</summary>
    public bool? IsKey { get; init; }

    /// <summary>Сцена заблокирована от правок: <c>null</c> — не менять (ТЗ п. 27).</summary>
    public bool? IsLocked { get; init; }

    /// <summary>Заметки сцены: <c>null</c> — не менять (ТЗ п. 27).</summary>
    public string? Notes { get; init; }

    /// <summary>
    /// Шаги сюжетных линий, к которым относится сцена (ADR 012). <c>null</c> — не менять.
    /// Список полный: этапы, которых в нём нет, теряют эту сцену.
    /// </summary>
    public IReadOnlyList<Guid>? PlotLineStepIds { get; init; }
}

/// <summary>
/// Запрос на изменение настроек проекта и приложения (ТЗ п. 27: тема, шрифт, размер,
/// автосохранение, язык, резервные копии). Набор значений полный: экран настроек
/// передаёт согласованное состояние целиком.
/// </summary>
public sealed record UpdateProjectSettingsRequest
{
    public required ScriSelfStudio.Core.Model.AppTheme Theme { get; init; }

    public required string FontFamily { get; init; }

    public required double FontSize { get; init; }

    public required bool AutoSaveEnabled { get; init; }

    public required int AutoSaveIntervalSeconds { get; init; }

    public required bool BackupsEnabled { get; init; }

    public required string Language { get; init; }
}

/// <summary>Найденный файл восстановления после аварийного завершения (ТЗ п. 27, 28).</summary>
public sealed record RecoveryInfo
{
    public required string ProjectFilePath { get; init; }

    public required string RecoveryFilePath { get; init; }

    public required DateTimeOffset WrittenAt { get; init; }

    public required Guid ProjectId { get; init; }

    public required string ProjectName { get; init; }
}

/// <summary>
/// Владелец открытого проекта (ТЗ п. 22): UI получает снимки и отправляет команды.
/// </summary>
public interface IProjectService
{
    /// <summary>Снимок открытого проекта либо null, если проект не открыт.</summary>
    ProjectDto? Current { get; }

    string? CurrentFilePath { get; }

    /// <summary>
    /// Формат файла проекта, если он отличается от нашего (например, старый ZIP-контейнер).
    /// <c>null</c> — собственный формат приложения (ТЗ п. 5).
    /// </summary>
    string? CurrentSourceFormat { get; }

    bool IsDirty { get; }

    Task<Result<ProjectDto>> CreateAsync(CreateProjectRequest request, CancellationToken cancellationToken = default);

    Task<Result<ProjectDto>> OpenAsync(string filePath, CancellationToken cancellationToken = default);

    Task<Result> SaveAsync(CancellationToken cancellationToken = default);

    Task<Result> SaveAsAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Закрывает проект. При наличии несохранённых изменений требуется явное
    /// подтверждение пользователя (ТЗ п. 14: диалог о несохранённых изменениях).
    /// </summary>
    Task<Result> CloseAsync(bool discardChanges, CancellationToken cancellationToken = default);

    /// <summary>
    /// Применяет доменную операцию к открытому проекту и помечает проект изменённым.
    /// Изменение живёт в памяти, запись на диск выполняет сохранение (ТЗ п. 22, 23).
    /// </summary>
    Result<ProjectDto> Mutate(Func<Project, Result> change);

    /// <summary>Копия документа сцены для редактора; изменения не влияют на проект до Commit.</summary>
    Result<SceneDocumentDto> GetSceneDocument(Guid sceneId);

    Task<Result<SceneDocumentDto>> UpdateSceneDocumentAsync(UpdateSceneDocumentRequest request, CancellationToken cancellationToken = default);

    Task<Result<ProjectDto>> UpdateSceneAsync(UpdateSceneRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Применяет настройки к открытому проекту и сразу записывает их на диск:
    /// настройки — явное действие пользователя, их потеря при закрытии недопустима (ТЗ п. 27).
    /// </summary>
    Task<Result<ProjectDto>> UpdateSettingsAsync(UpdateProjectSettingsRequest request, CancellationToken cancellationToken = default);

    Task<Result<RecoveryInfo?>> DetectRecoveryAsync(string filePath, CancellationToken cancellationToken = default);

    Task<Result<ProjectDto>> RecoverAsync(RecoveryInfo recovery, CancellationToken cancellationToken = default);

    Task<Result> DiscardRecoveryAsync(RecoveryInfo recovery, CancellationToken cancellationToken = default);
}
