using System;
using Dumpify;
using Spectre.Console;
using ScratchpadSharp.Core.External.NetPad.Presentation;
using ScratchpadSharp.Core.External.NetPad.Presentation.Html;

namespace ScratchpadSharp.Core.Services;

public class DumpDispatcher : IDumpSink
{
    private static Action<object?, string?>? _htmlRenderer;
    private static Action<string>? _plainTextAppender;

    public static void RegisterHtmlRenderer(Action<object?, string?> renderer)
    {
        _htmlRenderer = renderer;
    }

    public static void RegisterPlainTextAppender(Action<string> appender)
    {
        _plainTextAppender = appender;
    }

    private static void AppendPlainText(string text)
    {
        if (!string.IsNullOrEmpty(text))
            _plainTextAppender?.Invoke(text);
    }

    public static void DispatchHtml(string html)
    {
        if (_htmlRenderer != null)
        {
            // Call the renderer with pre-rendered HTML
            _htmlRenderer(html, null);
        }
    }

    public static void Dispatch<T>(T obj, string? label = null)
    {
        if (_htmlRenderer != null)
        {
            // Serialize to HTML using HtmlPresenter, preserving the label/title
            var options = label != null ? new DumpOptions { Title = label } : null;
            string html = HtmlPresenter.Serialize(obj, options);
            _htmlRenderer(html, null);

            // Console strings are captured separately; other types get plain-text for the Text view.
            if (obj is not string)
                AppendPlainText(PlainTextPresenter.Format(obj, label));
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

    public void ResultWrite<T>(T? o, DumpOptions? options = null)
    {
        // Serialize the object to HTML using NetPad's HtmlPresenter
        string html = HtmlPresenter.Serialize(o, options);

        // Dispatch the HTML string to be rendered by the UI
        DispatchHtml(html);
        AppendPlainText(PlainTextPresenter.Format(o, options?.Title));
    }

    public void SqlWrite<T>(T? o, DumpOptions? options = null)
    {
        // Treat SQL dump same as regular dump for now
        ResultWrite(o, options);
    }
}
