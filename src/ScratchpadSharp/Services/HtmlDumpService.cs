using System;
using System.Net;
using System.Text;
using Dumpify;
using ScratchpadSharp.Core.Services;

namespace ScratchpadSharp.Services;

public class HtmlDumpService
{
    private Action<string>? _updateCallback;
    private readonly string _htmlLoopTemplate;

    private readonly StringBuilder _contentBuffer = new StringBuilder();

    public HtmlDumpService()
    {
        // Register this service as the HTML renderer in the core dispatcher
        DumpDispatcher.RegisterHtmlRenderer(RenderHtml);

        // Load NetPad styles from embedded resource in Core assembly
        var assembly = typeof(DumpDispatcher).Assembly;
        var resourceName = "ScratchpadSharp.Core.External.NetPad.Presentation.NetPadStyles.css";

        // Debugging: List all resources
        foreach (var name in assembly.GetManifestResourceNames())
        {
            // System.Diagnostics.Debug.WriteLine($"Resource: {name}");
            _updateCallback?.Invoke($"Resource found: {name}");
        }

        string css = "";
        using (var stream = assembly.GetManifestResourceStream(resourceName))
        {
            if (stream != null)
            {
                using var reader = new System.IO.StreamReader(stream);
                css = reader.ReadToEnd();
            }
            else
            {
                // Fallback if resource not found (shouldn't happen if build is correct)
                css = "/* Error loading NetPad styles */";
            }
        }

        _htmlLoopTemplate = $@"
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

    public void SetUpdateCallback(Action<string> callback)
    {
        _updateCallback = callback;
    }

    public void Clear()
    {
        _contentBuffer.Clear();
        // Invoke with empty template to clear the view but keep styles/structure ready
        var output = _htmlLoopTemplate.Replace("{{BODY}}", string.Empty);
        _updateCallback?.Invoke(output);
    }

    private void RenderHtml(object? data, string? label)
    {
        try
        {
            // The dispatcher sends the HTML string as the first argument
            string htmlContent = data as string ?? data?.ToString() ?? string.Empty;

            // Append the new content to our buffer
            _contentBuffer.Append(htmlContent);

            // Wrap the full accumulated content in our template with styles
            var output = _htmlLoopTemplate.Replace("{{BODY}}", _contentBuffer.ToString());
            _updateCallback?.Invoke(output);
        }
        catch (Exception ex)
        {
            // For errors, we might want to append them too, or just log them.
            // Let's append a red error message to the buffer so the user sees it in context.
            var errorHtml = $"<div style='color:red'>Error rendering HTML: {ex.Message}</div>";
            _contentBuffer.Append(errorHtml);
            var output = _htmlLoopTemplate.Replace("{{BODY}}", _contentBuffer.ToString());
            _updateCallback?.Invoke(output);
        }
    }
}
