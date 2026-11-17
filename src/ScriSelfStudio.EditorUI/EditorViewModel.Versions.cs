namespace ScriSelfStudio.EditorUI;

using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.ComponentModel;

public sealed partial class EditorViewModel
{
    [ObservableProperty] private int _restoredDocumentGeneration;

    [RelayCommand]
    private async Task OpenVersionsAsync()
    {
        var target = GetSceneAtCaretLine(_ribbonCaretLine)?.Id ?? _engine.SceneId;
        if (target is not { } sceneId) return;
        StopAutosave();
        try
        {
            if (!(await CommitRibbonDocumentsAsync(CancellationToken.None)).IsSuccess) return;
            var saved = await _projects.SaveAsync();
            if (!saved.IsSuccess) { ReportInPlace(saved.Error!); return; }
            var previous = _projects.GetSceneDocument(sceneId).Require().Revision;
            _suppressRibbonRefresh = true;
            try { await _versionsDialog.ShowAsync(sceneId); }
            finally { _suppressRibbonRefresh = false; }
            var current = _projects.GetSceneDocument(sceneId);
            if (current.IsSuccess && current.Require().Revision != previous)
            {
                var opened = await _engine.OpenSceneAsync(sceneId);
                if (!opened.IsSuccess) { ReportInPlace(opened.Error!); return; }
                RebuildRibbon();
                RestoredDocumentGeneration++;
            }
        }
        finally { StartAutosave(); }
    }
}
