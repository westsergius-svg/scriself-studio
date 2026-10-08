namespace ScriSelfStudio.Core.Model;

/// <summary>Тема оформления приложения (ТЗ п. 6, 27).</summary>
public enum AppTheme
{
    System = 0,
    Light = 1,
    Dark = 2,

    /// <summary>Базовая тёмно-графитовая тема редактора (макет «Studio»).</summary>
    Studio = 3,

    /// <summary>Светлая бумажная тема.</summary>
    Paper = 4,

    /// <summary>Тёмная нейтральная тема.</summary>
    Graphite = 5,

    /// <summary>Холодная сине-зелёная тема.</summary>
    Ocean = 6,

    /// <summary>Глубокая зелёная тема.</summary>
    Forest = 7,
}
