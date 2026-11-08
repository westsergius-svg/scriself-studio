namespace ScriSelfStudio.Contracts.Dialogs;

public interface ISceneVersionsDialogService
{
    Task ShowAsync(Guid sceneId);
}
