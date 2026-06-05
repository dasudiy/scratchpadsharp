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
        /// <summary>Character offset in the editor where user code begins (after extracted usings/comments).</summary>
        public required int UserCodeStartInEditor { get; init; }
        /// <summary>Character offset in <see cref="FullText"/> where user code begins.</summary>
        public required int UserCodeStartInDocument { get; init; }
        public required List<string> EffectiveUsings { get; init; }
    }

    public static ScriptDocument Build(string editorCode, IReadOnlyList<string> configUsings)
    {
        var (cleanCode, userUsings, removedLineCount) = ScriptPreprocessor.ExtractUsingsAndComments(editorCode);

        var effectiveUsings = configUsings
            .Concat(userUsings)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var usingsBlock = string.Join(Environment.NewLine, effectiveUsings.Select(u => $"using {u};"));
        var lineDirective = $"#line {removedLineCount + 1} \"Script.cs\"";

        var wrapperBefore = (usingsBlock.Length > 0 ? usingsBlock + "\n\n" : "") + @"
public class __ScriptRunner
{
    public static string __ConnectionString { get; set; } = string.Empty;

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
        var userCodeStartInEditor = FindUserCodeStartInEditor(editorCode, cleanCode);
        var userCodeStartInDocument = wrapperBefore.Length;

        return new ScriptDocument
        {
            FullText = fullText,
            UserCodeStartInEditor = userCodeStartInEditor,
            UserCodeStartInDocument = userCodeStartInDocument,
            EffectiveUsings = effectiveUsings
        };
    }

    public static int ToDocumentPosition(ScriptDocument doc, int editorPosition)
    {
        if (editorPosition <= doc.UserCodeStartInEditor)
            return doc.UserCodeStartInDocument;

        return doc.UserCodeStartInDocument + (editorPosition - doc.UserCodeStartInEditor);
    }

    public static int ToEditorPosition(ScriptDocument doc, int documentPosition)
    {
        if (documentPosition < doc.UserCodeStartInDocument)
            return doc.UserCodeStartInEditor;

        return doc.UserCodeStartInEditor + (documentPosition - doc.UserCodeStartInDocument);
    }

    private static int FindUserCodeStartInEditor(string editorCode, string cleanCode)
    {
        if (string.IsNullOrEmpty(cleanCode))
            return 0;

        var index = editorCode.IndexOf(cleanCode, StringComparison.Ordinal);
        return index >= 0 ? index : 0;
    }
}
