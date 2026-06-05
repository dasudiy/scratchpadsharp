using System;
using System.Collections.Generic;
using System.Linq;

namespace ScratchpadSharp.Core.Services;

public class ScriptPreprocessor
{
    private static readonly string[] separator = ["\r\n", "\r", "\n"];

    public static (string CleanCode, List<string> Usings, int RemovedLineCount, int EditorCodeStartOffset) ExtractUsingsAndComments(string code)
    {
        var lines = code.Split(separator, StringSplitOptions.None);
        var usings = new List<string>();
        var cleanLines = new List<string>();
        var inBlockComment = false;
        var codeStarted = false;
        var removedLineCount = 0;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            if (inBlockComment)
            {
                if (trimmed.Contains("*/"))
                {
                    inBlockComment = false;
                }
                removedLineCount++;
                continue;
            }

            if (trimmed.StartsWith("/*"))
            {
                if (!trimmed.EndsWith("*/"))
                {
                    inBlockComment = true;
                }
                removedLineCount++;
                continue;
            }

            if (trimmed.StartsWith("//") || string.IsNullOrWhiteSpace(trimmed))
            {
                if (codeStarted)
                {
                    cleanLines.Add(line);
                }
                else
                {
                    removedLineCount++;
                }
                continue;
            }

            if (!codeStarted && trimmed.StartsWith("using ", StringComparison.Ordinal))
            {
                if (trimmed.EndsWith(";"))
                {
                    var ns = trimmed[6..^1].Trim();
                    usings.Add(ns);
                }
                removedLineCount++;
                continue;
            }

            codeStarted = true;
            cleanLines.Add(line);
        }

        var cleanCode = string.Join(Environment.NewLine, cleanLines);
        var editorCodeStartOffset = !codeStarted || string.IsNullOrEmpty(cleanCode)
            ? code.Length
            : code.IndexOf(cleanCode, StringComparison.Ordinal);

        if (editorCodeStartOffset < 0)
        {
            editorCodeStartOffset = 0;
        }

        return (cleanCode, usings, removedLineCount, editorCodeStartOffset);
    }
}
