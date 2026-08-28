using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace ScratchpadSharp.ViewModels;

public sealed class QueryTreeIconConverter : IValueConverter
{
    public static readonly QueryTreeIconConverter Instance = new();

    private static readonly Geometry FolderIcon = Geometry.Parse("M2 4h6l2 2h10v12H2V4z");
    private static readonly Geometry FileIcon = Geometry.Parse("M6 2h8l4 4v14H6V2zm8 0v4h4");
    private static readonly Geometry PackageIcon = Geometry.Parse("M4 4h16v4H4V4zm0 6h16v10H4V10z");

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value switch
        {
            QueryNodeKind.Directory => FolderIcon,
            QueryNodeKind.ScriptFile => FileIcon,
            QueryNodeKind.PackageFile or QueryNodeKind.FolderPackage => PackageIcon,
            _ => FileIcon
        };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
