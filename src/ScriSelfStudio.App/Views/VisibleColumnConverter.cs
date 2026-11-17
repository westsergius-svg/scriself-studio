namespace ScriSelfStudio.App.Views;

using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Data.Converters;

/// <summary>Скрытая адаптивная панель не занимает свою колонку и место разделителя.</summary>
public sealed class VisibleColumnConverter : IValueConverter
{
    public GridLength Width { get; set; } = new(1, GridUnitType.Star);
    public double MinWidth { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (targetType == typeof(GridLength))
        {
            return value is true ? Width : new GridLength(0);
        }
        return value is true ? MinWidth : 0d;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => BindingOperations.DoNothing;
}
