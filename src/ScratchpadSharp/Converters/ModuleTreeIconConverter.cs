using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Svg.Skia;
using ScratchpadSharp.Shared.Models;
using ScratchpadSharp.ViewModels;

namespace ScratchpadSharp.Converters;

/// <summary>
/// Tree icons sourced from JetBrains intellij-community (Apache 2.0).
/// </summary>
public static class ModuleTreeIcons
{
    private const string Base = "avares://ScratchpadSharp/Assets/Icons/module-tree/";

    private static readonly Dictionary<string, SvgSource> Sources = new(StringComparer.Ordinal);

    public static IImage? GetImage(ModuleTreeNode? node)
    {
        if (node is null || node.IsLoading || node.NodeKind == "Loading")
            return null;

        var source = GetSource(ResolveFile(node));
        return source is null ? null : new SvgImage { Source = source };
    }

    private static string ResolveFile(ModuleTreeNode node) => node.NodeKind switch
    {
        "Instance" when node.ProviderId == DatabaseProviderIds.SqlServer => "sqlServer.svg",
        "Instance" when node.ProviderId == DatabaseProviderIds.Sqlite => "sqlite.svg",
        "Instance" => "database.svg",
        "Type" => "efCore.svg",
        "TableFolder" => "folder.svg",
        "Table" => "table.svg",
        "View" => "view.svg",
        "Column" => "column.svg",
        "Error" => "error.svg",
        _ => "database.svg"
    };

    private static SvgSource? GetSource(string file)
    {
        if (Sources.TryGetValue(file, out var cached))
            return cached;

        try
        {
            var source = SvgSource.Load(Base + file);
            Sources[file] = source;
            return source;
        }
        catch
        {
            return null;
        }
    }
}

public sealed class ModuleTreeIconImageConverter : IValueConverter
{
    public static readonly ModuleTreeIconImageConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is ModuleTreeNode node ? ModuleTreeIcons.GetImage(node) : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
