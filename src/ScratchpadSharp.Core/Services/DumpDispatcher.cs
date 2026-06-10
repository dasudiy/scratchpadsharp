using System;
using Dumpify;
using Spectre.Console;
using ScratchpadSharp.Core.External.NetPad.Presentation;
using ScratchpadSharp.Core.External.NetPad.Presentation.Html;

namespace ScratchpadSharp.Core.Services;

public class DumpDispatcher : IDumpSink
{
    private readonly Action<object?, string?>? _htmlRenderer;
    private readonly Action<string>? _plainTextAppender;

    public DumpDispatcher(Action<object?, string?> htmlRenderer, Action<string> plainTextAppender)
    {
        _htmlRenderer = htmlRenderer;
        _plainTextAppender = plainTextAppender;
    }

    public void DispatchHtml(string html)
    {
        if (_htmlRenderer != null)
            _htmlRenderer(html, null);
    }

    public void Dispatch<T>(T obj, string? label = null)
    {
        if (_htmlRenderer != null)
        {
            var options = label != null ? new DumpOptions { Title = label } : null;
            string html = HtmlPresenter.Serialize(obj, options);
            _htmlRenderer(html, null);

            if (obj is not string)
                AppendPlainText(PlainTextPresenter.Format(obj, label));
        }
        else
        {
            AnsiRender(obj, label);
        }
    }

    private void AppendPlainText(string text)
    {
        if (!string.IsNullOrEmpty(text))
            _plainTextAppender?.Invoke(text);
    }

    private static void AnsiRender<T>(T obj, string? label)
    {
        if (!string.IsNullOrEmpty(label))
            AnsiConsole.MarkupLine($"[bold yellow]{label}[/]");

        obj.Dump(label, renderer: Renderers.Table);
    }

    public void ResultWrite<T>(T? o, DumpOptions? options = null)
    {
        string html = HtmlPresenter.Serialize(o, options);
        DispatchHtml(html);
        AppendPlainText(PlainTextPresenter.Format(o, options?.Title));
    }

    public void SqlWrite<T>(T? o, DumpOptions? options = null)
    {
        ResultWrite(o, options);
    }
}
