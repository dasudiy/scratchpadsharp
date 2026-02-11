using System;
using Dumpify;
using Spectre.Console;

namespace ScratchpadSharp.Core.Services;

public static class DumpDispatcher
{
    private static Action<object?, string?>? _htmlRenderer;

    public static void RegisterHtmlRenderer(Action<object?, string?> renderer)
    {
        _htmlRenderer = renderer;
    }

    public static void Dispatch<T>(T obj, string? label = null)
    {
        if (_htmlRenderer != null)
        {
            _htmlRenderer(obj, label);
        }
        else
        {
            AnsiRender(obj, label);
        }
    }

    private static void AnsiRender<T>(T obj, string? label)
    {
        if (!string.IsNullOrEmpty(label))
        {
            AnsiConsole.MarkupLine($"[bold yellow]{label}[/]");
        }

        // Use Dumpify's Table renderer explicitly.
        obj.Dump(label, renderer: Renderers.Table);
    }
}
