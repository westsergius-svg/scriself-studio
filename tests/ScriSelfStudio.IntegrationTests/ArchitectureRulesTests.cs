namespace ScriSelfStudio.IntegrationTests;

using System.Reflection;

/// <summary>
/// Автоматическая проверка правил зависимостей (ТЗ п. 12, 15, 33, 34).
/// </summary>
public sealed class ArchitectureRulesTests
{
    private const string AvaloniaPrefix = "Avalonia";
    private const string PersistenceAssembly = "ScriSelfStudio.Persistence";

    public static TheoryData<string> DomainAssemblies() =>
    [
        "ScriSelfStudio.Contracts",
        "ScriSelfStudio.Core",
        "ScriSelfStudio.EditorEngine",
        "ScriSelfStudio.BookArchitecture",
        "ScriSelfStudio.Persistence",
    ];

    public static TheoryData<string> UiAssemblies() =>
    [
        "ScriSelfStudio.Presentation",
        "ScriSelfStudio.Launcher",
        "ScriSelfStudio.EditorUI",
        "ScriSelfStudio.ArchitectureUI",
        "ScriSelfStudio.StoryUI",
    ];

    [Theory]
    [MemberData(nameof(DomainAssemblies))]
    public void Domain_assemblies_do_not_depend_on_avalonia(string assemblyName)
    {
        var references = ReferencedAssemblyNames(assemblyName);

        Assert.DoesNotContain(references, name => name.StartsWith(AvaloniaPrefix, StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(UiAssemblies))]
    public void Ui_modules_do_not_depend_on_persistence_implementation(string assemblyName)
    {
        var references = ReferencedAssemblyNames(assemblyName);

        Assert.DoesNotContain(PersistenceAssembly, references);
    }

    [Fact]
    public void App_is_the_only_project_that_registers_implementations()
    {
        var appReferences = ReferencedAssemblyNames("ScriSelfStudio");

        Assert.Contains(PersistenceAssembly, appReferences);
        Assert.Contains("ScriSelfStudio.Launcher", appReferences);
    }

    private static IReadOnlyList<string> ReferencedAssemblyNames(string assemblyName) =>
        [.. Assembly.Load(assemblyName)
            .GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .Where(name => name.Length > 0)];
}
