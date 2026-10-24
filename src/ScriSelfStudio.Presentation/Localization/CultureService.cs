namespace ScriSelfStudio.Presentation.Localization;

using System.Globalization;

/// <summary>
/// Применение выбранного языка к форматам даты и чисел, а также к строкам
/// интерфейса (ТЗ п. 27). Русский остаётся языком по умолчанию.
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
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
            Current = language;
            LocalizationManager.Instance.SetLanguage(language);
        }
        catch (CultureNotFoundException)
        {
            // Неизвестный код языка: текущая культура не меняется, настройка остаётся сохранённой.
        }
    }
}
