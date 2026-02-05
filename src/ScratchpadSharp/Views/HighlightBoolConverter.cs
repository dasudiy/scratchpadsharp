using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace ScratchpadSharp.Views;

public class HighlightBoolConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isHighlighted && parameter is string colorParam)
        {
            var colors = colorParam.Split('|');
            if (colors.Length == 2)
            {
                var highlightedColor = colors[0];
                var normalColor = colors[1];
                
                var brush = isHighlighted
                    ? new SolidColorBrush(Color.Parse(highlightedColor))
                    : new SolidColorBrush(Color.Parse(normalColor));
                
                return brush;
            }
        }

        return new SolidColorBrush(Colors.Black);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
