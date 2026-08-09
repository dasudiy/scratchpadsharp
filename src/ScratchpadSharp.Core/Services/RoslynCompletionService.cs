using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Completion;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Tags;
using Microsoft.CodeAnalysis.Text;
using ScratchpadSharp.Shared.Models;

namespace ScratchpadSharp.Core.Services;

public class CompletionChangeInfo(
    ImmutableArray<TextChange> textChanges,
    int? newPosition,
    bool includesCommitCharacter,
    ImmutableArray<string> newUsings = default)
{
    public ImmutableArray<TextChange> TextChanges { get; } = textChanges;
    public int? NewPosition { get; } = newPosition;
    public bool IncludesCommitCharacter { get; } = includesCommitCharacter;
    /// <summary>Namespace names that should be added as using directives (e.g. "Newtonsoft.Json")</summary>
    public ImmutableArray<string> NewUsings { get; } = newUsings.IsDefault ? ImmutableArray<string>.Empty : newUsings;
}

public interface IRoslynCompletionService
{
    Task<CompletionResult> GetCompletionsAsync(
        string tabId,
        string code,
        int position,
        ProjectContext context,
        bool forceInvoke = false,
        CancellationToken cancellationToken = default);

    Task<CompletionChangeInfo> GetCompletionChangeAsync(
        string tabId,
        string code,
        CompletionItem item,
        List<string> usings,
        CancellationToken cancellationToken = default);

    Task<string?> GetCompletionDescriptionAsync(
        string tabId,
        CompletionItem item,
        CancellationToken cancellationToken = default);
}

public class CompletionResult
{
    public ImmutableArray<EnhancedCompletionItem> Items { get; set; }
    public string? TriggerCharacter { get; set; }
    public bool IsIncomplete { get; set; }
}

public class EnhancedCompletionItem
{
    public CompletionItem RoslynItem { get; set; } = null!;
    public string DisplayText { get; set; } = string.Empty;
    public string SortText { get; set; } = string.Empty;
    public string FilterText { get; set; } = string.Empty;
    public string? InlineDescription { get; set; }
    public CompletionItemKind Kind { get; set; }
    public int Priority { get; set; }
    public bool IsRecommended { get; set; }
    public string? Documentation { get; set; }
    public ImmutableArray<string> Tags { get; set; }
    public TextSpan CompletionSpan { get; set; }
}

public enum CompletionItemKind
{
    Class,
    Struct,
    Interface,
    Enum,
    Delegate,
    Method,
    Property,
    Field,
    Event,
    Constant,
    Variable,
    Parameter,
    Keyword,
    Snippet,
    Namespace,
    Module,
    Constructor,
    ExtensionMethod,
    EnumMember,
    TypeParameter
}

public class RoslynCompletionService : IRoslynCompletionService
{
    private const int DefaultDebounceMs = 100;
    private const int MaxCompletionItems = 1000;

    public async Task<CompletionResult> GetCompletionsAsync(
        string tabId,
        string code,
        int position,
        ProjectContext context,
        bool forceInvoke = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!RoslynWorkspaceService.Instance.IsInitialized)
            {
                System.Diagnostics.Debug.WriteLine("[Completion] Workspace not initialized yet");
                return new CompletionResult { Items = [] };
            }

            if (context == null)
            {
                System.Diagnostics.Debug.WriteLine("[Completion] ProjectContext is null");
                return new CompletionResult { Items = [] };
            }

            await RoslynWorkspaceService.Instance.UpdateReferencesAsync(tabId, context.AbsoluteCompileReferences);

            await RoslynWorkspaceService.Instance.UpdateDocumentAsync(tabId, code, context.EffectiveUsings.ToList());

            var document = RoslynWorkspaceService.Instance.GetDocument(tabId);

            var scriptDocument = RoslynWorkspaceService.Instance.BuildScriptDocument(code, context.EffectiveUsings.ToList());
            var adjustedPosition = ScriptDocumentBuilder.ToDocumentPosition(scriptDocument, position);

            // Get completion service
            var completionService = CompletionService.GetService(document);
            if (completionService == null)
            {
                System.Diagnostics.Debug.WriteLine("[Completion] CompletionService is null");
                return new CompletionResult { Items = [] };
            }

            // 确定触发字符
            string? triggerChar = null;
            SourceText documentText = SourceText.From("");
            if (adjustedPosition > 0)
            {
                documentText = await document.GetTextAsync(cancellationToken);
                var ch = documentText[adjustedPosition - 1];
                if (char.IsWhiteSpace(ch) || ch == '.' || ch == '(' || ch == '[' || ch == '<')
                {
                    triggerChar = ch.ToString();
                }
            }

            var trigger = forceInvoke
                ? CompletionTrigger.Invoke
                : triggerChar != null
                    ? CompletionTrigger.CreateInsertionTrigger(triggerChar[0])
                    : CompletionTrigger.Invoke;

            var completions = await completionService.GetCompletionsAsync(
                document,
                adjustedPosition,
                trigger,
                cancellationToken: cancellationToken);

            if (completions == null || completions.ItemsList.Count == 0)
            {
                System.Diagnostics.Debug.WriteLine($"[Completion] No completions at position {position}");
                return new CompletionResult { Items = [] };
            }

            var isMemberAccess = !forceInvoke && (triggerChar == "."
                || IsMemberAccessContext(documentText, adjustedPosition));
            var isUsingDirective = IsUsingDirectiveContext(documentText, adjustedPosition);

            var meaningfulItems = completions.ItemsList
                .Where(i => isUsingDirective || !i.Tags.Contains(WellKnownTags.Keyword))
                .Where(i => !isMemberAccess || !i.Tags.Contains(WellKnownTags.Snippet))
                .ToImmutableArray();

            if (meaningfulItems.Length == 0)
            {
                return new CompletionResult { Items = [] };
            }

            System.Diagnostics.Debug.WriteLine($"[Completion] Found {meaningfulItems.Length} meaningful items");

            if (documentText.Length == 0)
                documentText = await document.GetTextAsync(cancellationToken);

            var filterPrefix = GetTypedFilterPrefix(documentText, adjustedPosition, meaningfulItems);

            var enhancedItems = EnhanceCompletionItems(
                meaningfulItems,
                scriptDocument,
                isMemberAccess,
                cancellationToken);

            var sortedItems = ApplyPrioritySort(enhancedItems, filterPrefix).ToList();
            var limitedItems = ApplyResultLimit(sortedItems, MaxCompletionItems, filterPrefix)
                .ToImmutableArray();

            System.Diagnostics.Debug.WriteLine($"[Completion] Returning {limitedItems.Length} enhanced items (prefix='{filterPrefix}')");

            return new CompletionResult
            {
                Items = limitedItems,
                TriggerCharacter = triggerChar,
                IsIncomplete = sortedItems.Count > MaxCompletionItems
            };
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Completion] Error: {ex.Message}");
            return new CompletionResult { Items = ImmutableArray<EnhancedCompletionItem>.Empty };
        }
    }

    public async Task<CompletionChangeInfo> GetCompletionChangeAsync(
        string tabId,
        string code,
        CompletionItem item,
        List<string> usings,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Update document with current code to ensure changes are calculated against correct state
            await RoslynWorkspaceService.Instance.UpdateDocumentAsync(tabId, code, usings);

            var document = RoslynWorkspaceService.Instance.GetDocument(tabId);
            if (document == null) return new CompletionChangeInfo(ImmutableArray<TextChange>.Empty, null, false);

            var completionService = CompletionService.GetService(document);
            if (completionService == null)
                return new CompletionChangeInfo(ImmutableArray<TextChange>.Empty, null, false);

            var change = await completionService.GetChangeAsync(document, item, cancellationToken: cancellationToken);

            var scriptDocument = RoslynWorkspaceService.Instance.BuildScriptDocument(code, usings);

            var adjustedChanges = new List<TextChange>();
            var newUsings = new List<string>();
            var existingUsingNamespaces = new HashSet<string>(usings, StringComparer.Ordinal);

            foreach (var textChange in change.TextChanges)
            {
                if (textChange.Span.Start < scriptDocument.ConfigUsingsSectionLength)
                {
                    TryExtractUsingsFromText(textChange.NewText, existingUsingNamespaces, newUsings);
                    continue;
                }

                var editorStart = ScriptDocumentBuilder.ToEditorPosition(scriptDocument, textChange.Span.Start);
                adjustedChanges.Add(new TextChange(
                    new TextSpan(editorStart, textChange.Span.Length),
                    textChange.NewText ?? string.Empty));
            }

            if (newUsings.Count == 0)
            {
                TryAddImportNamespaceFromItem(item, existingUsingNamespaces, newUsings);
            }

            if (newUsings.Count == 0)
            {
                var ns = await TryFindNamespaceForTypeAsync(document, item.DisplayText, cancellationToken);
                if (!string.IsNullOrEmpty(ns) && existingUsingNamespaces.Add(ns))
                    newUsings.Add(ns);
            }

            return new CompletionChangeInfo(
                adjustedChanges.ToImmutableArray(),
                change.NewPosition.HasValue
                    ? ScriptDocumentBuilder.ToEditorPosition(scriptDocument, change.NewPosition.Value)
                    : null,
                change.IncludesCommitCharacter,
                newUsings.ToImmutableArray());
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Completion] Error getting change: {ex.Message}");
            return new CompletionChangeInfo(ImmutableArray<TextChange>.Empty, null, false);
        }
    }


    public async Task<string?> GetCompletionDescriptionAsync(
        string tabId,
        CompletionItem item,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var document = RoslynWorkspaceService.Instance.GetDocument(tabId);
            if (document == null) return null;

            var completionService = CompletionService.GetService(document);
            if (completionService == null) return null;

            var description = await completionService.GetDescriptionAsync(document, item, cancellationToken);
            return description?.Text;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Completion] Error getting description: {ex.Message}");
            return null;
        }
    }

    private static List<EnhancedCompletionItem> EnhanceCompletionItems(
        ImmutableArray<CompletionItem> items,
        ScriptDocumentBuilder.ScriptDocument scriptDocument,
        bool isMemberAccess,
        CancellationToken cancellationToken)
    {
        var enhanced = new List<EnhancedCompletionItem>();

        foreach (var item in items)
        {
            var span = item.Span;
            var editorStart = ScriptDocumentBuilder.ToEditorPosition(scriptDocument, span.Start);
            var adjustedSpan = new TextSpan(Math.Max(0, editorStart), span.Length);

            var enhancedItem = new EnhancedCompletionItem
            {
                RoslynItem = item,
                DisplayText = item.DisplayText,
                SortText = item.SortText,
                FilterText = item.FilterText,
                InlineDescription = item.InlineDescription,
                Tags = item.Tags.ToImmutableArray(),
                Kind = DetermineCompletionKind(item.Tags),
                Priority = CalculateBasePriority(item, isMemberAccess),
                CompletionSpan = adjustedSpan
            };

            // Description is now loaded lazily
            // enhancedItem.Documentation = ... 


            // 标记推荐项
            enhancedItem.IsRecommended = IsRecommendedItem(item);

            enhanced.Add(enhancedItem);
        }

        return enhanced;
    }

    private static CompletionItemKind DetermineCompletionKind(ImmutableArray<string> tags)
    {
        // 按优先级检查标签
        if (tags.Contains(WellKnownTags.Class)) return CompletionItemKind.Class;
        if (tags.Contains(WellKnownTags.Structure)) return CompletionItemKind.Struct;
        if (tags.Contains(WellKnownTags.Interface)) return CompletionItemKind.Interface;
        if (tags.Contains(WellKnownTags.Enum)) return CompletionItemKind.Enum;
        if (tags.Contains(WellKnownTags.Delegate)) return CompletionItemKind.Delegate;
        if (tags.Contains(WellKnownTags.Method))
        {
            if (tags.Contains(WellKnownTags.ExtensionMethod))
                return CompletionItemKind.ExtensionMethod;
            return CompletionItemKind.Method;
        }

        if (tags.Contains(WellKnownTags.Property)) return CompletionItemKind.Property;
        if (tags.Contains(WellKnownTags.Field)) return CompletionItemKind.Field;
        if (tags.Contains(WellKnownTags.Event)) return CompletionItemKind.Event;
        if (tags.Contains(WellKnownTags.Constant)) return CompletionItemKind.Constant;
        if (tags.Contains(WellKnownTags.Local)) return CompletionItemKind.Variable;
        if (tags.Contains(WellKnownTags.Parameter)) return CompletionItemKind.Parameter;
        if (tags.Contains(WellKnownTags.Keyword)) return CompletionItemKind.Keyword;
        if (tags.Contains(WellKnownTags.Snippet)) return CompletionItemKind.Snippet;
        if (tags.Contains(WellKnownTags.Namespace)) return CompletionItemKind.Namespace;
        if (tags.Contains(WellKnownTags.Module)) return CompletionItemKind.Module;
        if (tags.Contains(WellKnownTags.EnumMember)) return CompletionItemKind.EnumMember;
        if (tags.Contains(WellKnownTags.TypeParameter)) return CompletionItemKind.TypeParameter;

        return CompletionItemKind.Method; // 默认
    }

    private static int CalculateBasePriority(CompletionItem item, bool isMemberAccess)
    {
        int priority = 0;

        // Roslyn的MatchPriority
        priority += (int)(item.Rules.MatchPriority * 1000);

        // 根据标签调整优先级
        var tags = item.Tags;

        // 关键字和片段优先级高
        if (tags.Contains(WellKnownTags.Keyword))
            priority += 5000;
        if (tags.Contains(WellKnownTags.Snippet))
            priority += 4000;

        // 本地变量和参数优先级较高
        if (tags.Contains(WellKnownTags.Local))
            priority += 3000;
        if (tags.Contains(WellKnownTags.Parameter))
            priority += 2900;

        if (isMemberAccess)
        {
            // After '.': members before types
            if (tags.Contains(WellKnownTags.Property))
                priority += 2000;
            if (tags.Contains(WellKnownTags.Method))
                priority += 1900;
            if (tags.Contains(WellKnownTags.Field))
                priority += 1800;
            if (tags.Contains(WellKnownTags.Class))
                priority += 1000;
            if (tags.Contains(WellKnownTags.Interface))
                priority += 900;
            if (tags.Contains(WellKnownTags.Structure))
                priority += 800;
            if (tags.Contains(WellKnownTags.Enum))
                priority += 700;
        }
        else
        {
            // Identifier / type position: types before arbitrary members so
            // BCL types like Regex are not crowded out by NuGet method spam.
            if (tags.Contains(WellKnownTags.Class))
                priority += 2000;
            if (tags.Contains(WellKnownTags.Interface))
                priority += 1900;
            if (tags.Contains(WellKnownTags.Structure))
                priority += 1800;
            if (tags.Contains(WellKnownTags.Enum))
                priority += 1700;
            if (tags.Contains(WellKnownTags.Property))
                priority += 1000;
            if (tags.Contains(WellKnownTags.Method))
                priority += 900;
            if (tags.Contains(WellKnownTags.Field))
                priority += 800;
        }

        // 命名空间（便于 using 和限定名补全）
        if (tags.Contains(WellKnownTags.Namespace))
            priority += 1050;

        // 扩展方法稍微降低优先级
        if (tags.Contains(WellKnownTags.ExtensionMethod))
            priority -= 100;

        return priority;
    }

    private static bool IsRecommendedItem(CompletionItem item)
    {
        // Roslyn标记的推荐项
        if (item.Rules.MatchPriority == MatchPriority.Preselect)
            return true;

        // 常用的关键字和模式
        var displayText = item.DisplayText;
        if (item.Tags.Contains(WellKnownTags.Keyword))
        {
            var commonKeywords = new HashSet<string>
            {
                "if", "else", "for", "foreach", "while", "return",
                "var", "new", "this", "base", "true", "false", "null"
            };
            return commonKeywords.Contains(displayText);
        }

        // 常用的类型和方法
        var commonTypes = new HashSet<string>
        {
            "string", "int", "bool", "double", "DateTime", "List",
            "Dictionary", "Task", "async", "await"
        };
        return commonTypes.Contains(displayText);
    }

    private static bool IsUsingDirectiveContext(SourceText text, int position)
    {
        var lineStart = position;
        while (lineStart > 0 && text[lineStart - 1] != '\n' && text[lineStart - 1] != '\r')
            lineStart--;

        var lineSlice = text.ToString(TextSpan.FromBounds(lineStart, position));
        return lineSlice.TrimStart().StartsWith("using ", StringComparison.Ordinal);
    }

    private static void TryExtractUsingsFromText(
        string? text,
        HashSet<string> existingUsingNamespaces,
        List<string> newUsings)
    {
        if (string.IsNullOrEmpty(text))
            return;

        foreach (var line in text.Split('\n').Select(l => l.Trim('\r', ' ')))
        {
            if (!TryParseUsingNamespace(line, out var ns))
                continue;

            if (!string.IsNullOrEmpty(ns) && existingUsingNamespaces.Add(ns))
                newUsings.Add(ns);
        }
    }

    private static bool TryParseUsingNamespace(string line, out string ns)
    {
        ns = string.Empty;
        if (!line.StartsWith("using ", StringComparison.Ordinal) || !line.EndsWith(';'))
            return false;

        var body = line[6..^1].Trim();
        if (body.StartsWith("static ", StringComparison.Ordinal))
            body = body[7..].Trim();

        if (string.IsNullOrEmpty(body))
            return false;

        ns = body;
        return true;
    }

    private static async Task<string?> TryFindNamespaceForTypeAsync(
        Document document,
        string typeName,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(typeName) || typeName.Contains('.'))
            return null;

        var compilation = await document.Project.GetCompilationAsync(cancellationToken);
        if (compilation == null)
            return null;

        // GetTypesByMetadataName is an indexed lookup — much faster than recursive tree walk
        var best = compilation.GetTypesByMetadataName(typeName)
            .Where(t => t.DeclaredAccessibility == Accessibility.Public)
            .OrderByDescending(t =>
                t.ContainingNamespace?.ToDisplayString()
                    ?.StartsWith("System", StringComparison.Ordinal) == true ? 1 : 0)
            .FirstOrDefault();

        var namespaceName = best?.ContainingNamespace?.ToDisplayString();
        return string.IsNullOrEmpty(namespaceName) || namespaceName == "<global namespace>"
            ? null
            : namespaceName;
    }

    private static void TryAddImportNamespaceFromItem(
        CompletionItem item,
        HashSet<string> existingUsingNamespaces,
        List<string> newUsings)
    {
        if (item.Tags.Contains(WellKnownTags.Namespace))
            return;

        // Roslyn's import-completion items (unimported types) carry the containing
        // namespace verbatim in InlineDescription, e.g. "System.Text.RegularExpressions"
        // for Regex. Use it as-is rather than stripping a segment.
        var ns = item.InlineDescription;
        if (string.IsNullOrEmpty(ns) || ns == "<global namespace>")
            return;

        if (existingUsingNamespaces.Add(ns))
            newUsings.Add(ns);
    }

    private static bool IsMemberAccessContext(SourceText text, int position)
    {
        var start = position;
        while (start > 0 && IsIdentifierPart(text[start - 1]))
            start--;

        return start > 0 && text[start - 1] == '.';
    }

    private static bool IsIdentifierPart(char ch) => char.IsLetterOrDigit(ch) || ch == '_';

    private static string GetTypedFilterPrefix(
        SourceText text,
        int position,
        ImmutableArray<CompletionItem> items)
    {
        if (text.Length == 0 || items.Length == 0)
            return string.Empty;

        var start = items[0].Span.Start;
        if (start < 0 || start > text.Length)
            return string.Empty;

        var end = Math.Clamp(position, start, text.Length);
        return end > start
            ? text.ToString(TextSpan.FromBounds(start, end))
            : string.Empty;
    }

    private static int GetPrefixMatchRank(EnhancedCompletionItem item, string? filterPrefix)
    {
        if (string.IsNullOrEmpty(filterPrefix))
            return 0;

        var text = !string.IsNullOrEmpty(item.FilterText) ? item.FilterText : item.DisplayText;
        if (text.Equals(filterPrefix, StringComparison.OrdinalIgnoreCase))
            return 3;
        if (text.StartsWith(filterPrefix, StringComparison.OrdinalIgnoreCase))
            return 2;
        if (text.Contains(filterPrefix, StringComparison.OrdinalIgnoreCase))
            return 1;
        return 0;
    }

    private static bool MatchesFilterPrefix(EnhancedCompletionItem item, string filterPrefix) =>
        GetPrefixMatchRank(item, filterPrefix) >= 2;

    private static IEnumerable<EnhancedCompletionItem> ApplyResultLimit(
        List<EnhancedCompletionItem> sortedItems,
        int maxItems,
        string filterPrefix)
    {
        if (string.IsNullOrEmpty(filterPrefix))
        {
            var namespaces = sortedItems
                .Where(i => i.Tags.Contains(WellKnownTags.Namespace))
                .ToList();
            var others = sortedItems
                .Where(i => !i.Tags.Contains(WellKnownTags.Namespace))
                .Take(Math.Max(0, maxItems - namespaces.Count));

            return namespaces.Concat(others).Take(maxItems);
        }

        // Always keep prefix matches (Regex for "Reg") so NuGet type spam cannot
        // push them past MaxCompletionItems before AvaloniaEdit filters the list.
        var prefixMatches = sortedItems.Where(i => MatchesFilterPrefix(i, filterPrefix)).ToList();
        if (prefixMatches.Count >= maxItems)
            return prefixMatches.Take(maxItems);

        var remaining = sortedItems
            .Where(i => !MatchesFilterPrefix(i, filterPrefix))
            .Take(maxItems - prefixMatches.Count);

        return prefixMatches.Concat(remaining);
    }

    private static IEnumerable<EnhancedCompletionItem> ApplyPrioritySort(
        List<EnhancedCompletionItem> items,
        string filterPrefix)
    {
        return items
            .OrderByDescending(i => i.IsRecommended)
            .ThenByDescending(i => GetPrefixMatchRank(i, filterPrefix))
            .ThenByDescending(i => i.Priority)
            .ThenBy(i => i.SortText)
            .ThenBy(i => i.DisplayText);
    }
}