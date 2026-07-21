using System;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace ScratchpadSharp.Core.Services;

/// <summary>
/// Converts common ANSI SGR sequences (including Spectre.Console output) into simple HTML.
/// </summary>
public static partial class AnsiToHtml
{
    private static readonly Regex AnsiRegex = AnsiEscapeRegex();

    public static string Convert(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var sb = new StringBuilder();
        sb.Append("""<pre style="margin:0;padding:12px 8px;font-family:JetBrains Mono,Cascadia Code,Consolas,monospace;font-size:12.5px;white-space:pre-wrap;word-wrap:break-word;color:#1F2328;">""");

        var color = "#1F2328";
        var background = (string?)null;
        var bold = false;

        var lastIndex = 0;
        foreach (Match match in AnsiRegex.Matches(text))
        {
            AppendEscaped(sb, text.AsSpan(lastIndex, match.Index - lastIndex), color, background, bold);
            ApplySgr(match.Groups[1].Value, ref color, ref background, ref bold);
            lastIndex = match.Index + match.Length;
        }

        AppendEscaped(sb, text.AsSpan(lastIndex), color, background, bold);
        sb.Append("</pre>");
        return sb.ToString();
    }

    /// <summary>Strip ANSI sequences for plain-text consumers.</summary>
    public static string Strip(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;
        return AnsiRegex.Replace(text, string.Empty);
    }

    private static void ApplySgr(string codes, ref string color, ref string? background, ref bool bold)
    {
        if (string.IsNullOrEmpty(codes))
        {
            color = "#1F2328";
            background = null;
            bold = false;
            return;
        }

        var parts = codes.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (var i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], out var code))
                continue;

            switch (code)
            {
                case 0:
                    color = "#1F2328";
                    background = null;
                    bold = false;
                    break;
                case 1:
                    bold = true;
                    break;
                case 22:
                    bold = false;
                    break;
                case 39:
                    color = "#1F2328";
                    break;
                case 49:
                    background = null;
                    break;
                case >= 30 and <= 37:
                    color = BasicFg(code - 30, bright: false);
                    break;
                case >= 90 and <= 97:
                    color = BasicFg(code - 90, bright: true);
                    break;
                case >= 40 and <= 47:
                    background = BasicBg(code - 40, bright: false);
                    break;
                case >= 100 and <= 107:
                    background = BasicBg(code - 100, bright: true);
                    break;
                case 38 when i + 1 < parts.Length:
                    if (parts[i + 1] == "5" && i + 2 < parts.Length && int.TryParse(parts[i + 2], out var c256))
                    {
                        color = From256(c256);
                        i += 2;
                    }
                    else if (parts[i + 1] == "2" && i + 4 < parts.Length
                             && int.TryParse(parts[i + 2], out var r)
                             && int.TryParse(parts[i + 3], out var g)
                             && int.TryParse(parts[i + 4], out var b))
                    {
                        color = $"#{r:X2}{g:X2}{b:X2}";
                        i += 4;
                    }
                    break;
                case 48 when i + 1 < parts.Length:
                    if (parts[i + 1] == "5" && i + 2 < parts.Length && int.TryParse(parts[i + 2], out var bc256))
                    {
                        background = From256(bc256);
                        i += 2;
                    }
                    else if (parts[i + 1] == "2" && i + 4 < parts.Length
                             && int.TryParse(parts[i + 2], out var br)
                             && int.TryParse(parts[i + 3], out var bg)
                             && int.TryParse(parts[i + 4], out var bb))
                    {
                        background = $"#{br:X2}{bg:X2}{bb:X2}";
                        i += 4;
                    }
                    break;
            }
        }
    }

    private static void AppendEscaped(StringBuilder sb, ReadOnlySpan<char> text, string color, string? background, bool bold)
    {
        if (text.IsEmpty)
            return;

        sb.Append("<span style=\"color:");
        sb.Append(color);
        if (background != null)
        {
            sb.Append(";background-color:");
            sb.Append(background);
        }
        if (bold)
            sb.Append(";font-weight:bold");
        sb.Append("\">");
        sb.Append(WebUtility.HtmlEncode(text.ToString()));
        sb.Append("</span>");
    }

    private static string BasicFg(int index, bool bright) => index switch
    {
        0 => bright ? "#6E7681" : "#24292F",
        1 => bright ? "#FF7B72" : "#CF222E",
        2 => bright ? "#3FB950" : "#1A7F37",
        3 => bright ? "#D29922" : "#9A6700",
        4 => bright ? "#58A6FF" : "#0969DA",
        5 => bright ? "#BC8CFF" : "#8250DF",
        6 => bright ? "#39C5CF" : "#1B7C83",
        _ => bright ? "#F0F6FC" : "#1F2328"
    };

    private static string BasicBg(int index, bool bright) => index switch
    {
        0 => bright ? "#484F58" : "#D0D7DE",
        1 => bright ? "#DA3633" : "#FFEBE9",
        2 => bright ? "#238636" : "#DAFBE1",
        3 => bright ? "#9E6A03" : "#FFF8C5",
        4 => bright ? "#1F6FEB" : "#DDF4FF",
        5 => bright ? "#8957E5" : "#FBEFFF",
        6 => bright ? "#1B7C83" : "#D0F3F5",
        _ => bright ? "#6E7681" : "#F6F8FA"
    };

    private static string From256(int index)
    {
        if (index < 0) return "#1F2328";
        if (index < 8) return BasicFg(index, bright: false);
        if (index < 16) return BasicFg(index - 8, bright: true);
        if (index < 232)
        {
            var n = index - 16;
            var r = n / 36;
            var g = (n % 36) / 6;
            var b = n % 6;
            static int Level(int v) => v == 0 ? 0 : 55 + v * 40;
            return $"#{Level(r):X2}{Level(g):X2}{Level(b):X2}";
        }

        var gray = 8 + (index - 232) * 10;
        gray = Math.Clamp(gray, 0, 255);
        return $"#{gray:X2}{gray:X2}{gray:X2}";
    }

    [GeneratedRegex(@"\x1B\[([0-9;]*)m", RegexOptions.Compiled)]
    private static partial Regex AnsiEscapeRegex();
}
