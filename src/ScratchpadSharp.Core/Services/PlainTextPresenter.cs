using System.Text;
using Dumpify;

namespace ScratchpadSharp.Core.Services;

internal static class PlainTextPresenter
{
    public static string Format(object? value, string? title = null)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrEmpty(title))
            sb.AppendLine(title);

        if (value == null)
            sb.AppendLine("(null)");
        else if (value is string text)
            sb.AppendLine(text);
        else
            sb.Append(DumpExtensions.DumpText(value));

        if (sb.Length > 0 && sb[^1] != '\n')
            sb.AppendLine();
        sb.AppendLine();
        return sb.ToString();
    }
}
