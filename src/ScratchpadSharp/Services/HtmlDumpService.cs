using System;
using System.Net;
using Dumpify;
using ScratchpadSharp.Core.Services;

namespace ScratchpadSharp.Services;

public class HtmlDumpService
{
    private Action<string>? _updateCallback;
    private readonly string _htmlLoopTemplate;

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
        _updateCallback?.Invoke(string.Empty);
    }

    private void RenderHtml(object? data, string? label)
    {
        try
        {
            // The dispatcher sends the HTML string as the first argument
            string htmlContent = data as string ?? data?.ToString() ?? string.Empty;

            // Wrap the HTML content in our template with styles
            var output = _htmlLoopTemplate.Replace("{{BODY}}", htmlContent);
            _updateCallback?.Invoke(output);
        }
        catch (Exception ex)
        {
            _updateCallback?.Invoke($"<div style='color:red'>Error rendering HTML: {ex.Message}</div>");
        }
    }
}
