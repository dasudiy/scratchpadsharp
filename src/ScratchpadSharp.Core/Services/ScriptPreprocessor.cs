using System;
using System.Collections.Generic;

namespace ScratchpadSharp.Core.Services;

public class ScriptPreprocessor
{
    private static readonly string[] LineSeparators = ["\r\n", "\r", "\n"];

    public static (string CleanCode, List<string> Usings, int RemovedLineCount, int EditorCodeStartOffset) ExtractUsingsAndComments(string code)
    {
        var lines = code.Split(LineSeparators, StringSplitOptions.None);
        var usings = new List<string>();
        var cleanLines = new List<string>();
        var inBlockComment = false;
        var codeStarted = false;
        var removedLineCount = 0;
        // Track the start of the first executable line by walking the original string.
        // Do NOT use IndexOf(cleanCode) — short prefixes like "R"/"Reg" also appear inside
        // using directives (e.g. System.Text.RegularExpressions).
        var editorCodeStartOffset = code.Length;
        var lineStartOffset = 0;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            var isLeadingTrivia = false;

            if (inBlockComment)
            {
                if (trimmed.Contains("*/"))
                    inBlockComment = false;
                isLeadingTrivia = !codeStarted;
            }
            else if (trimmed.StartsWith("/*"))
            {
                if (!trimmed.EndsWith("*/"))
                    inBlockComment = true;
                isLeadingTrivia = !codeStarted;
            }
            else if (trimmed.StartsWith("//") || string.IsNullOrWhiteSpace(trimmed))
            {
                isLeadingTrivia = !codeStarted;
            }
            else if (!codeStarted && trimmed.StartsWith("using ", StringComparison.Ordinal))
            {
                if (trimmed.EndsWith(";"))
                {
                    var ns = trimmed[6..^1].Trim();
                    usings.Add(ns);
                }

                isLeadingTrivia = true;
            }

            if (isLeadingTrivia)
            {
                removedLineCount++;
            }
            else
            {
                if (!codeStarted)
                {
                    codeStarted = true;
                    editorCodeStartOffset = lineStartOffset;
                }

                cleanLines.Add(line);
            }

            lineStartOffset = AdvancePastLine(code, lineStartOffset, line.Length);
        }

        var cleanCode = string.Join(Environment.NewLine, cleanLines);
        return (cleanCode, usings, removedLineCount, editorCodeStartOffset);
    }

    private static int AdvancePastLine(string code, int lineStart, int lineLength)
    {
        var pos = lineStart + lineLength;
        if (pos >= code.Length)
            return code.Length;

        if (code[pos] == '\r')
            pos++;
        if (pos < code.Length && code[pos] == '\n')
            pos++;

        return pos;
    }
}
