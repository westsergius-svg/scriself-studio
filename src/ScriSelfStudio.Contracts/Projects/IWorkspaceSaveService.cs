namespace ScriSelfStudio.Contracts.Projects;

using ScriSelfStudio.Contracts.Errors;

/// <summary>Общая фиксация текста и всех панелей перед записью, сменой проекта и выходом.</summary>
public interface IWorkspaceSaveService
{
    /// <summary>Включает черновики панелей и несохранённую ревизию самого проекта.</summary>
    bool HasPendingEdits { get; }

    /// <summary>Успех разрешает переход; при ошибке или новой правке рабочее пространство сохраняется открытым.</summary>
    Task<Result> SaveAsync(CancellationToken cancellationToken = default);
}
