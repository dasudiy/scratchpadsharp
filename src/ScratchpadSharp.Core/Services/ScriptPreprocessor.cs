using System;
using System.Collections.Generic;
using System.Linq;

namespace ScratchpadSharp.Core.Services;

public class ScriptPreprocessor
{
    public (string CleanCode, List<string> Usings, int RemovedLineCount) ExtractUsingsAndComments(string code)
    {
        var lines = code.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
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

            if (!codeStarted && trimmed.StartsWith("using ") && trimmed.EndsWith(";"))
            {
                var ns = trimmed.Replace("using ", "").Replace(";", "").Trim();
                usings.Add(ns);
                removedLineCount++;
                continue;
            }

            codeStarted = true;
            cleanLines.Add(line);
        }

        var cleanCode = string.Join(Environment.NewLine, cleanLines);
        return (cleanCode, usings, removedLineCount);
    }
}
