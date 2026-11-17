namespace ScriSelfStudio.App.Services;

using ScriSelfStudio.App.Shell;
using ScriSelfStudio.Contracts.Errors;
using ScriSelfStudio.Contracts.Projects;
using ScriSelfStudio.EditorUI;

/// <summary>Фиксирует все черновики, затем записывает один согласованный снимок книги.</summary>
public sealed class WorkspaceSaveService(
    IProjectService projects,
    EditorViewModel editor,
    SceneContextViewModel context,
    ArchitectureHostViewModel architecture) : IWorkspaceSaveService, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public bool HasPendingEdits => projects.IsDirty || editor.IsDirty
        || context.HasPendingChanges || architecture.HasPendingChanges;

    public async Task<Result> SaveAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var committed = await editor.CommitPendingEditsAsync(cancellationToken);
            if (!committed.IsSuccess) return committed;
            committed = await context.FlushAsync(cancellationToken);
            if (!committed.IsSuccess) return committed;
            committed = await architecture.CommitPendingAsync(cancellationToken);
            if (!committed.IsSuccess) return committed;

            if (projects.IsDirty)
            {
                var saved = await projects.SaveAsync(cancellationToken);
                if (!saved.IsSuccess) return saved;
            }

            return HasPendingEdits ? Result.Failure(AppError.Create(ErrorCode.EditorOperationConflict,
                "Во время сохранения появились новые правки. Они остались открыты; повторите сохранение перед выходом или сменой проекта.",
                suggestedAction: "Повторите сохранение; книга остаётся открытой.")) : Result.Success;
        }
        finally { _gate.Release(); }
    }

    public void Dispose() => _gate.Dispose();
}
