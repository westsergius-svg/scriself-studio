namespace ScriSelfStudio.UiSmokeTests;

using Microsoft.Extensions.DependencyInjection;
using ScriSelfStudio.App.Composition;
using ScriSelfStudio.Contracts.Dialogs;
using ScriSelfStudio.TestSupport;

/// <summary>Сборка контейнера для тестов интерфейса с подменёнными диалогами.</summary>
internal static class ServiceProviderFactory
{
    public static ServiceProvider Create(string settingsDirectory, IFileDialogService? dialogs = null)
    {
        var services = new ServiceCollection();
        services.AddScriSelfStudio(settingsDirectory, useAvaloniaDialogs: false);
        if (dialogs is not null)
        {
            services.AddSingleton(dialogs);
        }

        var provider = services.BuildServiceProvider();
        ScriSelfStudioServices.RegisterModules(provider);
        return provider;
    }
}
