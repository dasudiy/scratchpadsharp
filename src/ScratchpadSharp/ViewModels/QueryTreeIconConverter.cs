using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace ScratchpadSharp.ViewModels;

public sealed class QueryTreeIconConverter : IValueConverter
{
    public static readonly QueryTreeIconConverter Instance = new();

    private static readonly Geometry FolderIcon =
        Geometry.Parse("M2 4h6l2 2h10v12H2V4z");

    private static readonly Geometry ScriptFolderIcon =
        Geometry.Parse("M2 4h6l2 2h10v8H2V4zm3 10h10v2H5v-2zm0-3h10v2H5v-2z");

    private static readonly Geometry PackageIcon =
        Geometry.Parse("M5 2h10l3 3v15H5V2zm8 0v3h3M8 7h8M8 10h8M8 13h5");

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value switch
        {
            QueryNodeKind.Directory => FolderIcon,
            QueryNodeKind.FolderPackage => ScriptFolderIcon,
            QueryNodeKind.PackageFile => PackageIcon,
            _ => FolderIcon
        };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class QueryTreeIconBrushConverter : IValueConverter
{
    public static readonly QueryTreeIconBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value switch
        {
            QueryNodeKind.FolderPackage => new SolidColorBrush(Color.Parse("#4C8BF5")),
            QueryNodeKind.PackageFile => new SolidColorBrush(Color.Parse("#C97A1A")),
            _ => new SolidColorBrush(Color.Parse("#8A9199"))
        };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
