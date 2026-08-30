using System;
using System.IO;
using System.Linq;
using System.Text;
using ScratchpadSharp.Core.External.NetPad.Presentation;
using ScratchpadSharp.Core.Services;

namespace ScratchpadSharp.Services;

public class HtmlDumpService
{
    private static readonly Lazy<string> Shell = new(LoadShell);
    private static readonly Lazy<string> HostJs = new(() =>
        LoadUiResource("output-pane.js") ?? string.Empty);

    private readonly object _bufferLock = new();
    private readonly StringBuilder _contentBuffer = new();
    private readonly StringBuilder _textBuffer = new();

    public IDumpSink DumpSink { get; }

    public static string ShellHtml => Shell.Value;

    public static string HostJavaScript => HostJs.Value;

    public string BuildDumpDocument()
    {
        lock (_bufferLock)
            return WrapDumpDocument(_contentBuffer.ToString());
    }

    public static string BuildTextDocument(string textHtml) =>
        $"""
        <!DOCTYPE html>
        <html>
        <head><meta charset="utf-8" /></head>
        <body style="margin:0;background:#ffffff;color:#1F2328;"><div style="padding:8px 12px;">{textHtml}</div></body>
        </html>
        """;

    public string BodyHtml
    {
        get
        {
            lock (_bufferLock)
                return _contentBuffer.ToString();
        }
    }

    public string TextOutput
    {
        get
        {
            lock (_bufferLock)
                return _textBuffer.ToString();
        }
    }

    public event Action<string>? FragmentAppended;
    public event Action? Cleared;

    public HtmlDumpService()
    {
        DumpSink = new DumpDispatcher(RenderHtml, AppendPlainText);
    }

    public void Clear()
    {
        lock (_bufferLock)
        {
            _contentBuffer.Clear();
            _textBuffer.Clear();
        }
        Cleared?.Invoke();
    }

    private void AppendPlainText(string text)
    {
        lock (_bufferLock)
            _textBuffer.Append(text);
    }

    private void RenderHtml(object? data, string? label)
    {
        try
        {
            var htmlContent = data as string ?? data?.ToString() ?? string.Empty;
            lock (_bufferLock)
                _contentBuffer.Append(htmlContent);
            FragmentAppended?.Invoke(htmlContent);
        }
        catch (Exception ex)
        {
            var errorHtml = $"<div style='color:red'>Error rendering HTML: {ex.Message}</div>";
            lock (_bufferLock)
                _contentBuffer.Append(errorHtml);
            FragmentAppended?.Invoke(errorHtml);
        }
    }

    private static string LoadShell()
    {
        var css = LoadCoreResource("NetPadStyles.css") ?? "/* Error loading NetPad styles */";
        var extraCss = LoadUiResource("output-pane-extra.css") ?? string.Empty;
        var js = LoadUiResource("output-pane.js") ?? string.Empty;
        return WrapDumpDocument(string.Empty, css, extraCss, js);
    }

    private static string WrapDumpDocument(string body, string? css = null, string? extraCss = null, string? js = null)
    {
        css ??= LoadCoreResource("NetPadStyles.css") ?? "/* Error loading NetPad styles */";
        extraCss ??= LoadUiResource("output-pane-extra.css") ?? string.Empty;
        js ??= LoadUiResource("output-pane.js") ?? string.Empty;

        return $"""
            <!DOCTYPE html>
            <html>
            <head>
            <meta charset="utf-8" />
            <style>
            {css}
            {extraCss}
            </style>
            </head>
            <body>
            <output-pane id="dump-pane">
                <div id="dump" class="dump-container-wrapper">{body}</div>
            </output-pane>
            <script>
            {js}
            </script>
            </body>
            </html>
            """;
    }

    private static string? LoadCoreResource(string fileName)
    {
        var assembly = typeof(DumpDispatcher).Assembly;
        return ReadResource(assembly, fileName);
    }

    private static string? LoadUiResource(string fileName)
    {
        var assembly = typeof(HtmlDumpService).Assembly;
        return ReadResource(assembly, fileName);
    }

    private static string? ReadResource(System.Reflection.Assembly assembly, string fileName)
    {
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(fileName, StringComparison.OrdinalIgnoreCase));
        if (resourceName == null)
            return null;

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
            return null;

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
