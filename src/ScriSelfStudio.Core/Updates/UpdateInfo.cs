namespace ScriSelfStudio.Core.Updates;

/// <summary>
/// Сборка обновления, найденная в канале: либо запись манифеста <c>latest.json</c>,
/// либо карточка из листинга каталога (резервный путь, как в Scriborium 1.1.3.3S).
/// </summary>
/// <param name="Version">Числовые части версии (<c>1.1.3.3S</c> → <c>1.1.3.3</c>).</param>
/// <param name="Display">Подпись версии для окна обновления.</param>
/// <param name="FileName">Имя файла установщика в каталоге обновлений.</param>
/// <param name="Url">Полный адрес установщика.</param>
/// <param name="Notes">Заметки к версии (может быть пустым).</param>
/// <param name="Source">Источник записи: <c>manifest</c> или <c>listing</c>.</param>
public sealed record UpdateInfo(
    IReadOnlyList<int> Version,
    string Display,
    string FileName,
    string Url,
    string Notes,
    string Source)
{
    /// <summary>Версия в виде <c>1.1.3.3</c> — для сравнения и сообщений.</summary>
    public string VersionText => string.Join('.', Version);
}
