namespace ScriSelfStudio.Contracts.Dtos;

/// <summary>Проекция живых связей в локальном порядке сцены; отсутствующие ID игнорируются.</summary>
public static class SceneStepOrdering
{
    public static IReadOnlyList<Guid> OrderedStepIds(this SceneDto scene, IEnumerable<PlotLineDto> lines)
    {
        var linked = lines.OrderBy(line => line.Order).SelectMany(line => line.Steps.OrderBy(step => step.Order))
            .Where(step => step.SceneIds.Contains(scene.Id)).Select(step => step.Id).Distinct().ToList();
        var known = linked.ToHashSet();
        return scene.PlotLineStepOrder.Where(known.Contains).Concat(linked).Distinct().ToList();
    }
}
