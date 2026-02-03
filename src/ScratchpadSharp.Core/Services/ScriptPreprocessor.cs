using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace ScratchpadSharp.Core.Services;

public class ScriptPreprocessor
{
    public (string CleanCode, List<string> Usings) ExtractUsingsAndComments(string code)
    {
        var lines = code.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        var usings = new List<string>();
        var cleanLines = new List<string>();
        var inBlockComment = false;
        var codeStarted = false;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            if (inBlockComment)
            {
                if (trimmed.Contains("*/"))
                {
                    inBlockComment = false;
                }
                continue;
            }

            if (trimmed.StartsWith("/*"))
            {
                if (!trimmed.EndsWith("*/"))
                {
                    inBlockComment = true;
                }
                continue;
            }

            if (trimmed.StartsWith("//") || string.IsNullOrWhiteSpace(trimmed))
            {
                if (codeStarted)
                {
                    cleanLines.Add(line);
                }
                continue;
            }

            if (!codeStarted && trimmed.StartsWith("using ") && trimmed.EndsWith(";"))
            {
                var ns = trimmed.Replace("using ", "").Replace(";", "").Trim();
                usings.Add(ns);
                continue;
            }

            codeStarted = true;
            cleanLines.Add(line);
        }

        var cleanCode = string.Join(Environment.NewLine, cleanLines);
        return (cleanCode, usings);
    }
}
