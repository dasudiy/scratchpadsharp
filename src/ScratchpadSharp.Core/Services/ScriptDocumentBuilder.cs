using System;
using System.Collections.Generic;
using System.Linq;

namespace ScratchpadSharp.Core.Services;

/// <summary>
/// Builds the Roslyn document text for script IntelliSense.
/// Mirrors the wrapper used by <see cref="ScriptExecutionService"/> so completion
/// sees the same semantic context as compilation (method body, static locals, etc.).
/// </summary>
public static class ScriptDocumentBuilder
{
    public sealed class ScriptDocument
    {
        public required string FullText { get; init; }
        /// <summary>Character offset in the editor where executable script code begins.</summary>
        public required int UserCodeStartInEditor { get; init; }
        /// <summary>Character offset in <see cref="FullText"/> where executable script code begins.</summary>
        public required int UserCodeStartInDocument { get; init; }
        /// <summary>Length of the hidden config-usings block at the start of <see cref="FullText"/>.</summary>
        public required int ConfigUsingsSectionLength { get; init; }
        public required List<string> EffectiveUsings { get; init; }
    }

    public static ScriptDocument Build(string editorCode, IReadOnlyList<string> configUsings)
    {
        var (cleanCode, userUsings, removedLineCount, editorCodeStartOffset) =
            ScriptPreprocessor.ExtractUsingsAndComments(editorCode);

        var effectiveUsings = configUsings
            .Concat(userUsings)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var configUsingsBlock = string.Join(Environment.NewLine, effectiveUsings.Select(u => $"using {u};"));
        if (configUsingsBlock.Length > 0)
        {
            configUsingsBlock += Environment.NewLine;
        }

        var editorLeading = editorCodeStartOffset > 0
            ? editorCode[..editorCodeStartOffset]
            : string.Empty;

        var lineDirective = $"#line {removedLineCount + 1} \"Script.cs\"";

        var wrapperBefore = configUsingsBlock + editorLeading + @"
public class __ScriptRunner
{
    public static async Task<object?> __Execute()
    {
    " + lineDirective + @"
        ";

        const string wrapperAfter = @"
#line hidden
        return null;
    }
}
";

        var fullText = wrapperBefore + cleanCode + wrapperAfter;

        return new ScriptDocument
        {
            FullText = fullText,
            UserCodeStartInEditor = editorCodeStartOffset,
            UserCodeStartInDocument = wrapperBefore.Length,
            ConfigUsingsSectionLength = configUsingsBlock.Length,
            EffectiveUsings = effectiveUsings
        };
    }

    public static int ToDocumentPosition(ScriptDocument doc, int editorPosition)
    {
        // Positions in the editor's leading section (usings/comments before script code)
        // map into the document's editorLeading block that sits between configUsingsBlock
        // and the class wrapper. <= because the boundary position itself is still in the
        // leading section (e.g. caret at end of "using System." before any script lines).
        if (editorPosition <= doc.UserCodeStartInEditor)
            return doc.ConfigUsingsSectionLength + editorPosition;

        return doc.UserCodeStartInDocument + (editorPosition - doc.UserCodeStartInEditor);
    }

    public static int ToEditorPosition(ScriptDocument doc, int documentPosition)
    {
        if (documentPosition < doc.ConfigUsingsSectionLength)
            return 0;

        if (documentPosition < doc.UserCodeStartInDocument)
            return documentPosition - doc.ConfigUsingsSectionLength;

        return doc.UserCodeStartInEditor + (documentPosition - doc.UserCodeStartInDocument);
    }
}
