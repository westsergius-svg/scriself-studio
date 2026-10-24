namespace ScriSelfStudio.IntegrationTests;

using Microsoft.Extensions.DependencyInjection;
using ScriSelfStudio.App;
using ScriSelfStudio.App.Composition;
using ScriSelfStudio.Contracts.Projects;
using ScriSelfStudio.Launcher;
using ScriSelfStudio.TestSupport;

/// <summary>
/// Запуск приложения с проектом в командной строке (ТЗ п. 27, 31).
/// </summary>
public sealed class StartupCommandLineTests
{
    [Fact]
    public void Project_argument_is_resolved_to_absolute_path()
    {
        var resolved = StartupCommandLine.ResolveProjectPath(["Рукопись.scri"]);

        Assert.NotNull(resolved);
        Assert.True(Path.IsPathFullyQualified(resolved));
        Assert.Equal("Рукопись.scri", Path.GetFileName(resolved));
    }

    [Fact]
    public void Avalonia_flags_are_ignored()
    {
        var resolved = StartupCommandLine.ResolveProjectPath(["--fbdev", "Рукопись.SCRI", "--verbose"]);

        Assert.NotNull(resolved);
        Assert.Equal("Рукопись.SCRI", Path.GetFileName(resolved));
    }

    [Theory]
    [InlineData("Рукопись.scri.bak")]
    [InlineData("Автосохранение.backup")]
    public void Backup_argument_is_resolved_to_absolute_path(string argument)
    {
        var resolved = StartupCommandLine.ResolveProjectPath([argument]);

        Assert.NotNull(resolved);
        Assert.True(Path.IsPathFullyQualified(resolved));
        Assert.Equal(argument, Path.GetFileName(resolved));
    }

    [Fact]
    public void Arguments_without_project_extension_are_ignored()
    {
        Assert.Null(StartupCommandLine.ResolveProjectPath(["--fbdev", "notes.txt", ""]));
        Assert.Null(StartupCommandLine.ResolveProjectPath([]));
    }

    [Fact]
    public async Task Launcher_opens_project_passed_from_command_line()
    {
        using var workspace = new TempWorkspace();
        var path = workspace.FilePath("Автозапуск.scri");
        using var provider = ScriSelfStudioServices.Build(workspace.Root, useAvaloniaDialogs: false);

        var projects = provider.GetRequiredService<IProjectService>();
        var created = await projects.CreateAsync(new CreateProjectRequest { FilePath = path, Name = "Автозапуск" });
        Assert.True(created.IsSuccess, created.Error?.TechnicalDetails);
        Assert.True((await projects.CloseAsync(discardChanges: false)).IsSuccess);

        var launcher = provider.GetRequiredService<LauncherViewModel>();
        await launcher.InitializeAsync();
        Assert.False(launcher.IsProjectOpen);

        await launcher.OpenProjectFromCommandLineAsync(path);

        Assert.True(launcher.IsProjectOpen);
        Assert.Equal("Автозапуск", launcher.CurrentProjectTitle);
        Assert.False(launcher.HasError);
    }

    [Fact]
    public async Task Launcher_reports_missing_project_instead_of_failing()
    {
        using var workspace = new TempWorkspace();
        using var provider = ScriSelfStudioServices.Build(workspace.Root, useAvaloniaDialogs: false);

        var launcher = provider.GetRequiredService<LauncherViewModel>();
        await launcher.InitializeAsync();
        await launcher.OpenProjectFromCommandLineAsync(workspace.FilePath("Нет такого.scri"));

        Assert.False(launcher.IsProjectOpen);
        Assert.True(launcher.HasError);
    }
}
