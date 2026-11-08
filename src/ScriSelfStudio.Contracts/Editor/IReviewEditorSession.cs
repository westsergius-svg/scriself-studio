namespace ScriSelfStudio.Contracts.Editor;

using ScriSelfStudio.Core.Text;

public sealed record ReviewSelection(int SceneIndex, int Start, int Length);

/// <summary>Host of the visible editor's single undo history. All documents returned are copies.</summary>
public interface IReviewEditorSession
{
    event EventHandler? Changed;
    int CurrentSceneIndex { get; }
    ReviewSelection? Selection { get; }
    IReadOnlyList<RichDocument> ReadScenes();
    void ApplyScene(int sceneIndex, RichDocument document);
    void SelectRange(int sceneIndex, int start, int length);
}
