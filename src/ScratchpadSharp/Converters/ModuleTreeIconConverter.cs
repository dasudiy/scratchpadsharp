using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace ScratchpadSharp.Converters;

public sealed class ModuleTreeIconConverter : IValueConverter
{
    public static readonly ModuleTreeIconConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var kind = value as string;
        return kind switch
        {
            "Type" => Parse("M3 5a2 2 0 0 1 2-2h3.17a2 2 0 0 1 1.41.59l1.83 1.83A2 2 0 0 0 12.83 6H17a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5z"),
            "Instance" => Parse("M4 4h8v3H4V4zm0 5h8v7H4V9zm10-5h4a1 1 0 0 1 1 1v11a1 1 0 0 1-1 1h-4V4z"),
            "Table" => Parse("M3 4h10v1H3V4zm0 3h10v1H3V7zm0 3h10v1H3v-1zm0 3h10v1H3v-1zM2 3h12a1 1 0 0 1 1 1v8a1 1 0 0 1-1 1H2a1 1 0 0 1-1-1V4a1 1 0 0 1 1-1z"),
            "Column" => Parse("M6 3h2v10H6V3zm4 2h2v8h-2V5z"),
            "Error" => Parse("M7 2h2l6 10H1L7 2zm0 3.5L5.6 9h2.8L7 5.5zM6.5 10.5h1v1.5h-1v-1.5z"),
            "Loading" => Parse("M8 1.5A6.5 6.5 0 1 1 1.5 8H0a8 8 0 1 0 8-8v1.5z"),
            _ => Parse("M2 2h4v4H2V2zm6 0h4v4H8V2zM2 8h4v4H2V8zm6 0h4v4H8V8z")
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static StreamGeometry Parse(string data) => StreamGeometry.Parse(data);
}

public sealed class ModuleTreeIconBrushConverter : IValueConverter
{
    public static readonly ModuleTreeIconBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var kind = value as string;
        return kind switch
        {
            "Type" => new SolidColorBrush(Color.Parse("#586069")),
            "Instance" => new SolidColorBrush(Color.Parse("#2F81F7")),
            "Table" => new SolidColorBrush(Color.Parse("#1B1F23")),
            "Column" => new SolidColorBrush(Color.Parse("#8B929A")),
            "Error" => new SolidColorBrush(Color.Parse("#CF222E")),
            "Loading" => new SolidColorBrush(Color.Parse("#2F81F7")),
            _ => new SolidColorBrush(Color.Parse("#8B929A"))
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
