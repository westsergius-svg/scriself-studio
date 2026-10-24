namespace ScriSelfStudio.Presentation.Localization;

using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Markup.Xaml;

/// <summary>Переводит строку разметки и обновляет её при смене языка.</summary>
public sealed class TrExtension : MarkupExtension
{
    public TrExtension()
    {
    }

    public TrExtension(string key) => Key = key;

    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider) => new Binding
    {
        Source = LocalizationManager.Instance,
        Path = nameof(LocalizationManager.Language),
        Converter = TranslateConverter.Instance,
        ConverterParameter = Key,
        Mode = BindingMode.OneWay,
    };
}

/// <summary>Берёт исходную строку из параметра и переводит её по текущему языку.</summary>
public sealed class TranslateConverter : IValueConverter
{
    public static TranslateConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        LocalizationManager.Instance.Translate(parameter as string ?? value as string ?? string.Empty);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
