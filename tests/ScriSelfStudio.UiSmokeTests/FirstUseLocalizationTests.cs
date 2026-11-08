namespace ScriSelfStudio.UiSmokeTests;

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using ScriSelfStudio.Contracts.Projects;
using ScriSelfStudio.Contracts.Settings;
using ScriSelfStudio.EditorUI;
using ScriSelfStudio.Launcher;
using ScriSelfStudio.Presentation.Localization;

public sealed class FirstUseLocalizationTests
{
    [AvaloniaFact]
    public void Existing_project_summary_uses_English_and_Chinese_count_forms()
    {
        var culture = new CultureService();
        var command = new RelayCommand(() => { });
        var item = new RecentProjectItem(new RecentProjectEntry
        {
            FilePath = "project.scri",
            Name = "Название пользователя",
            LastOpenedAt = DateTimeOffset.Now,
            BookCount = 1,
            ChapterCount = 21,
            SceneCount = 101,
        }, command, command, command, command);
        try
        {
            culture.Apply("en-US");
            Assert.Equal("1 book · 21 chapters · 101 scenes", item.SummaryText);
            Assert.Equal("0 scenes", Loc.Count(0, "сцена", "сцены", "сцен"));
            Assert.Equal("11 scenes", Loc.Count(11, "сцена", "сцены", "сцен"));
            Assert.Equal("22 scenes", Loc.Count(22, "сцена", "сцены", "сцен"));
            culture.Apply("zh-CN");
            Assert.Equal("1 本书 · 21 章 · 101 个场景", item.SummaryText);
            Assert.Equal("0 个场景", Loc.Count(0, "сцена", "сцены", "сцен"));
            culture.Apply("ru-RU");
            Assert.Equal("1 книга · 21 глава · 101 сцена", item.SummaryText);
            Assert.Equal("11 сцен", Loc.Count(11, "сцена", "сцены", "сцен"));
            Assert.Equal("22 сцены", Loc.Count(22, "сцена", "сцены", "сцен"));
            Assert.Equal("Название пользователя", item.Name);
        }
        finally { culture.Apply("ru-RU"); }
    }

    [AvaloniaFact]
    public async Task Existing_editor_translates_tools_and_saves_without_changing_user_text()
    {
        using var context = await SceneContext.CreateAsync();
        var culture = context.Provider.GetRequiredService<ICultureService>();
        var model = context.Provider.GetRequiredService<EditorViewModel>();
        var projects = context.Provider.GetRequiredService<IProjectService>();
        var view = new EditorView { DataContext = model };
        var window = new Window { Content = view, Width = 1440, Height = 900 };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            culture.Apply("en-US");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Review", view.FindControl<Button>("ReviewPanelButton")!.Content);
            Assert.Equal("Versions", view.FindControl<Button>("SceneVersionsButton")!.Content);
            Assert.Equal("Spelling", view.FindControl<CheckBox>("SpellCheckToggle")!.Content);
            Assert.Equal("Grammar", view.FindControl<CheckBox>("GrammarCheckToggle")!.Content);

            model.TextTools.SetStatus("Орфография: незнакомых слов — {0}. Правой кнопкой — исправления.", 2);
            culture.Apply("zh-CN");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("审阅", view.FindControl<Button>("ReviewPanelButton")!.Content);
            Assert.Equal("拼写", view.FindControl<CheckBox>("SpellCheckToggle")!.Content);
            Assert.Equal("语法", view.FindControl<CheckBox>("GrammarCheckToggle")!.Content);
            Assert.StartsWith("拼写：2", model.TextTools.Status, StringComparison.Ordinal);

            const string sentence = "The lantern flickered beside the pier.";
            model.Text = sentence;
            await model.SaveCommand.ExecuteAsync(null);
            Assert.Equal("章节已保存。", model.StatusMessage);
            Assert.StartsWith("字符数：", model.CharactersLabel, StringComparison.Ordinal);
            var file = projects.CurrentFilePath!;
            Assert.True((await projects.CloseAsync(false)).IsSuccess);
            Assert.True((await projects.OpenAsync(file)).IsSuccess);
            Assert.Equal(sentence, projects.GetSceneDocument(context.SceneId).Require().Document.ToPlainText());
        }
        finally
        {
            window.Close();
            culture.Apply("ru-RU");
        }
    }
}
