namespace ScriSelfStudio.App.Services;

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using ScriSelfStudio.App.Views;
using ScriSelfStudio.Contracts.Dialogs;
using ScriSelfStudio.Contracts.Projects;
using ScriSelfStudio.EditorUI;

public sealed class AvaloniaSceneVersionsDialogService(IProjectService projects) : ISceneVersionsDialogService
{
    public async Task ShowAsync(Guid sceneId)
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime { MainWindow: { } owner })
            throw new InvalidOperationException("Не найдено главное окно для истории сцены.");
        using var model = new SceneVersionsViewModel(projects, sceneId);
        var window = new SceneVersionsWindow { DataContext = model };
        await window.ShowDialog(owner);
    }
}
