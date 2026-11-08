namespace ScriSelfStudio.Contracts.Dialogs;

/// <summary>Открывает встроенную справку без изменения текущего проекта и экрана.</summary>
public interface IHelpService
{
    void Show(string topicId = "start");
}
