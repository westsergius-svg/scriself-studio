namespace ScriSelfStudio.UiSmokeTests;

using Microsoft.Extensions.DependencyInjection;
using ScriSelfStudio.ArchitectureUI;
using ScriSelfStudio.Contracts.Editor;
using ScriSelfStudio.Launcher;
using ScriSelfStudio.TestSupport;

/// <summary>
/// Контекст тестов редактора сцены (шаги 3.1.x, ADR 003/004): проект в
/// временном workspace, открытая сцена и её движок из DI-контейнера.
/// </summary>
public sealed class SceneContext : IDisposable
{
    private SceneContext(TempWorkspace workspace, ServiceProvider provider, IEditorEngine engine, Guid sceneId)
    {
        Workspace = workspace;
        Provider = provider;
        Engine = engine;
        SceneId = sceneId;
    }

    public TempWorkspace Workspace { get; }

    public ServiceProvider Provider { get; }

    public IEditorEngine Engine { get; }

    public Guid SceneId { get; }

    public static async Task<SceneContext> CreateAsync()
    {
        var workspace = new TempWorkspace();
        var path = workspace.FilePath("Синхронизация.scri");
        var dialogs = new StubFileDialogService { CreatePath = path, PromptResult = "Элемент" };
        var provider = ServiceProviderFactory.Create(workspace.Root, dialogs);
        var launcher = provider.GetRequiredService<LauncherViewModel>();
        await launcher.InitializeAsync();
        await launcher.CreateProjectCommand.ExecuteAsync(null);
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

        var engine = provider.GetRequiredService<IEditorEngine>();
        if (engine.SceneId is not { } sceneId)
        {
            throw new InvalidOperationException("Движок не открыл сцену после OpenSceneCommand.");
        }

        return new SceneContext(workspace, provider, engine, sceneId);
    }

    public void Dispose()
    {
        Provider.Dispose();
        Workspace.Dispose();
    }
}