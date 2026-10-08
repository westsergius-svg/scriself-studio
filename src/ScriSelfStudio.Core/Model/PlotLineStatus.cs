namespace ScriSelfStudio.Core.Model;

/// <summary>
/// Стадия готовности сюжетной линии (макет экрана «Сюжетные линии»).
/// Значения фиксированы: они попадают в файл проекта как числа.
/// </summary>
public enum PlotLineStatus
{
    /// <summary>Идея — линия только задумана.</summary>
    Idea = 0,

    /// <summary>Черновик — линия набросана, но не проведена по тексту.</summary>
    Draft = 1,

    /// <summary>В работе — линия активно ведётся в рукописи.</summary>
    InProgress = 2,

    /// <summary>Готово — линия проведена и выверена.</summary>
    Done = 3,
}
