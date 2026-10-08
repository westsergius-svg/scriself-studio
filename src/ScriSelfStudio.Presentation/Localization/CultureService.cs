namespace ScriSelfStudio.Presentation.Localization;

using System.Globalization;

/// <summary>
/// Применение выбранного языка к форматам даты и чисел (ТЗ п. 27).
/// Перевод строк интерфейса — отдельный этап: строки сейчас русские.
/// </summary>
public interface ICultureService
{
    string Current { get; }

    void Apply(string language);
}

/// <inheritdoc />
public sealed class CultureService : ICultureService
{
    public string Current { get; private set; } = "ru-RU";

    public void Apply(string language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return;
        }

        try
        {
            var culture = CultureInfo.GetCultureInfo(language);
            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;
            Current = language;
        }
        catch (CultureNotFoundException)
        {
            // Неизвестный код языка: текущая культура не меняется, настройка остаётся сохранённой.
        }
    }
}
