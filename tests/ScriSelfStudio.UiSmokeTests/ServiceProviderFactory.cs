namespace ScriSelfStudio.UiSmokeTests;

using Microsoft.Extensions.DependencyInjection;
using ScriSelfStudio.App.Composition;
using ScriSelfStudio.Contracts.Dialogs;
using ScriSelfStudio.Core.Updates;
using ScriSelfStudio.TestSupport;

/// <summary>Сборка контейнера для тестов интерфейса с подменёнными диалогами.</summary>
internal static class ServiceProviderFactory
{
    public static ServiceProvider Create(
        string settingsDirectory,
        IFileDialogService? dialogs = null,
        IUpdateService? updates = null,
        ISceneVersionsDialogService? versionsDialog = null)
    {
        var services = new ServiceCollection();
        services.AddScriSelfStudio(settingsDirectory, useAvaloniaDialogs: false);
        if (versionsDialog is not null) services.AddSingleton(versionsDialog);
        if (dialogs is not null)
        {
            services.AddSingleton(dialogs);
        }

        if (updates is not null)
        {
            services.AddSingleton(updates);
        }

        var provider = services.BuildServiceProvider();
        ScriSelfStudioServices.RegisterModules(provider);
        return provider;
    }
}
