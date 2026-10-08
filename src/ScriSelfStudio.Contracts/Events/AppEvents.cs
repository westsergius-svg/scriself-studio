namespace ScriSelfStudio.Contracts.Events;

using ScriSelfStudio.Contracts.Books;
using ScriSelfStudio.Contracts.Errors;
using ScriSelfStudio.Core.Model;

/// <summary>Проект открыт (создан или загружен).</summary>
public sealed record ProjectOpened(Guid ProjectId, string FilePath, string Name);

/// <summary>
/// Проект импортирован из чужого формата. Предупреждения описывают, что именно
/// удалось перенести, а что восстановлено приблизительно (ТЗ п. 5).
/// </summary>
public sealed record ProjectImported(Guid ProjectId, string SourceFormat, IReadOnlyList<string> Warnings);

/// <summary>Проект закрыт.</summary>
public sealed record ProjectClosed(Guid ProjectId);

/// <summary>В проекте появились несохранённые изменения.</summary>
public sealed record ProjectChanged(Guid ProjectId, bool IsDirty);

/// <summary>Изменена структура книги.</summary>
public sealed record BookChanged(Guid? BookId, EntityKind Kind);

/// <summary>Изменена глава.</summary>
public sealed record ChapterChanged(Guid ChapterId);

/// <summary>Карточка персонажа создана или изменена (ADR 010).</summary>
public sealed record CharacterChanged(Guid CharacterId);

/// <summary>Сюжетная линия или её этапы изменены (ADR 010).</summary>
public sealed record PlotLineChanged(Guid PlotLineId);

/// <summary>Изменена сцена (включая текст).</summary>
public sealed record SceneChanged(Guid SceneId, long Revision);

/// <summary>Документ сцены изменён в редакторе.</summary>
public sealed record EditorDocumentChanged(Guid SceneId, long Revision, bool IsDirty);

/// <summary>Сохранение началось.</summary>
public sealed record SaveStarted(Guid ProjectId, string FilePath);

/// <summary>Сохранение завершилось успешно.</summary>
public sealed record SaveCompleted(Guid ProjectId, string FilePath, DateTimeOffset SavedAt);

/// <summary>Сохранение завершилось ошибкой.</summary>
public sealed record SaveFailed(Guid ProjectId, string FilePath, AppError Error);

/// <summary>Найден файл восстановления.</summary>
public sealed record RecoveryDetected(string FilePath, DateTimeOffset WrittenAt);

/// <summary>Тема оформления изменена.</summary>
public sealed record ThemeChanged(AppTheme Theme);
