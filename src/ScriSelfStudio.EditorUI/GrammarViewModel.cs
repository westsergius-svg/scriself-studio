namespace ScriSelfStudio.EditorUI;

using CommunityToolkit.Mvvm.ComponentModel;
using ScriSelfStudio.Contracts.Editor;
using ScriSelfStudio.Contracts.Settings;
using ScriSelfStudio.Presentation;
using ScriSelfStudio.Presentation.Localization;

public sealed partial class GrammarViewModel(IGrammarService service, IAppSettingsService settings) : ViewModelBase
{
    private bool _refreshing;
    [ObservableProperty] private bool _isEnabled = settings.Current.GrammarCheckEnabled;
    private string _status = "LanguageTool · локальные грамматические подсказки";
    private object?[] _statusArguments = [];
    public string Status
    {
        get => _statusArguments.Length == 0 ? Loc.T(_status) : Loc.F(_status, _statusArguments);
        set => SetStatus(value);
    }

    public void SetStatus(string message, params object?[] arguments)
    {
        _status = message;
        _statusArguments = arguments;
        OnPropertyChanged(nameof(Status));
    }

    public void RefreshLocalization() => OnPropertyChanged(nameof(Status));
    public IGrammarService Service { get; } = service;
    public string Language => settings.Current.GrammarLanguage is "ru" or "en-US" ? settings.Current.GrammarLanguage : "auto";
    public event EventHandler? PreferencesChanged;

    public void RefreshPreferences()
    {
        _refreshing = true;
        try { IsEnabled = settings.Current.GrammarCheckEnabled; }
        finally { _refreshing = false; }
        PreferencesChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnIsEnabledChanged(bool value)
    {
        if (!_refreshing) _ = SaveAsync(value);
        PreferencesChanged?.Invoke(this, EventArgs.Empty);
        if (!value) _ = StopAsync();
    }

    private async Task SaveAsync(bool value)
    {
        var result = await settings.UpdateAsync(p => p.GrammarCheckEnabled = value);
        if (!result.IsSuccess) SetStatus("Не удалось сохранить настройку: {0}", result.Error!.UserMessage);
    }

    public async Task StopAsync()
    {
        try { await Service.StopAsync(); }
        catch (ObjectDisposedException) { }
        catch (OperationCanceledException) { }
    }
}
