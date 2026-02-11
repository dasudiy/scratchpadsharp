using System;
using System.Net;
using System.Text.RegularExpressions;
using Dumpify;
using ScratchpadSharp.Core.Services;

namespace ScratchpadSharp.Services;

public class HtmlDumpService
{
    private Action<string>? _updateCallback;
    // Regex to strip ANSI escape sequences
    private static readonly Regex AnsiRegex = new Regex(@"\x1B\[[0-9;]*[a-zA-Z]", RegexOptions.Compiled);

    private const string CssStyles = @"
        <style>
            body { 
                background-color: #ffffff; 
                color: #000000; 
                font-family: 'Cascadia Code', Consolas, monospace; 
                padding: 10px; 
                margin: 0;
            }
            pre { 
                margin: 0; 
                white-space: pre-wrap;
            }
        </style>";

    public HtmlDumpService()
    {
        // Register this service as the HTML renderer in the core dispatcher
        DumpDispatcher.RegisterHtmlRenderer(RenderHtml);
    }

    public void SetUpdateCallback(Action<string> callback)
    {
        _updateCallback = callback;
    }

    public void Clear()
    {
        _updateCallback?.Invoke(string.Empty);
    }

    private void RenderHtml(object? obj, string? label)
    {
        try
        {
            // Dump using Table renderer (default) to get string representation
            var textWithAnsi = obj.DumpText(renderer: Renderers.Table);

            // Strip ANSI codes for clean text display in UI
            var text = AnsiRegex.Replace(textWithAnsi, "");

            // Encoded HTML with injected CSS styles
            var htmle = WebUtility.HtmlEncode(text);
            var html = $"{CssStyles}<pre>{htmle}</pre>";

            _updateCallback?.Invoke(html);
        }
        catch (Exception ex)
        {
            _updateCallback?.Invoke($"<div style='color:red'>Error rendering HTML: {ex.Message}</div>");
        }
    }
}
