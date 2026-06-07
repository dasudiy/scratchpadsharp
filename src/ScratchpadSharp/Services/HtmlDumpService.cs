using System;
using System.IO;
using System.Text;
using ScratchpadSharp.Core.External.NetPad.Presentation;
using ScratchpadSharp.Core.Services;

namespace ScratchpadSharp.Services;

public class HtmlDumpService
{
    private static readonly Lazy<string> HtmlLoopTemplate = new(LoadHtmlLoopTemplate);

    private Action<string>? _updateCallback;
    private readonly StringBuilder _contentBuffer = new();
    private readonly StringBuilder _textBuffer = new();

    public IDumpSink DumpSink { get; }

    public string TextOutput => _textBuffer.ToString();

    public HtmlDumpService()
    {
        DumpSink = new DumpDispatcher(RenderHtml, AppendPlainText);
    }

    public void SetUpdateCallback(Action<string> callback)
    {
        _updateCallback = callback;
    }

    public void Clear()
    {
        _contentBuffer.Clear();
        _textBuffer.Clear();
        var output = HtmlLoopTemplate.Value.Replace("{{BODY}}", string.Empty);
        _updateCallback?.Invoke(output);
    }

    private void AppendPlainText(string text)
    {
        _textBuffer.Append(text);
    }

    private void RenderHtml(object? data, string? label)
    {
        try
        {
            string htmlContent = data as string ?? data?.ToString() ?? string.Empty;
            _contentBuffer.Append(htmlContent);

            var output = HtmlLoopTemplate.Value.Replace("{{BODY}}", _contentBuffer.ToString());
            _updateCallback?.Invoke(output);
        }
        catch (Exception ex)
        {
            var errorHtml = $"<div style='color:red'>Error rendering HTML: {ex.Message}</div>";
            _contentBuffer.Append(errorHtml);
            var output = HtmlLoopTemplate.Value.Replace("{{BODY}}", _contentBuffer.ToString());
            _updateCallback?.Invoke(output);
        }
    }

    private static string LoadHtmlLoopTemplate()
    {
        var assembly = typeof(DumpDispatcher).Assembly;
        const string resourceName = "ScratchpadSharp.Core.External.NetPad.Presentation.NetPadStyles.css";

        var css = "/* Error loading NetPad styles */";
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream != null)
        {
            using var reader = new StreamReader(stream);
            css = reader.ReadToEnd();
        }

        return $@"
<!DOCTYPE html>
<html>
<head>
    <style>
        {css}
    </style>
</head>
<body>
    <output-pane>
        <div class='dump-container-wrapper'>
            {{{{BODY}}}}
        </div>
    </output-pane>
</body>
</html>";
    }
}
