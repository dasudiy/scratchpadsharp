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

namespace ScratchpadSharp.Core.Services;

public class CompletionChangeInfo
{
    public ImmutableArray<TextChange> TextChanges { get; }
    public int? NewPosition { get; }
    public bool IncludesCommitCharacter { get; }

    public CompletionChangeInfo(ImmutableArray<TextChange> textChanges, int? newPosition, bool includesCommitCharacter)
    {
        TextChanges = textChanges;
        NewPosition = newPosition;
        IncludesCommitCharacter = includesCommitCharacter;
    }
}

public interface IRoslynCompletionService
{
    Task<CompletionResult> GetCompletionsAsync(
        string tabId,
        string code,
        int position,
        List<string> usings,
        Dictionary<string, string> nugetPackages,
        CancellationToken cancellationToken = default);

    Task UpdateReferencesAsync(string tabId, Dictionary<string, string> nugetPackages);

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

    public async Task UpdateReferencesAsync(string tabId, Dictionary<string, string> nugetPackages)
    {
        await RoslynWorkspaceService.Instance.UpdateReferencesAsync(tabId, nugetPackages);
    }

    public async Task<CompletionResult> GetCompletionsAsync(
        string tabId,
        string code,
        int position,
        List<string> usings,
        Dictionary<string, string> nugetPackages,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var startTime = DateTime.Now;
            if (!RoslynWorkspaceService.Instance.IsInitialized)
            {
                System.Diagnostics.Debug.WriteLine("[Completion] Workspace not initialized yet");
                return new CompletionResult { Items = [] };
            }

            // Update references if packages are provided
            if (nugetPackages?.Count > 0)
            {
                await UpdateReferencesAsync(tabId, nugetPackages);
            }



            // Get the current document
            var document = RoslynWorkspaceService.Instance.GetDocument(tabId);

            // Calculate adjusted position
            var adjustedPosition = RoslynWorkspaceService.Instance.CalculateAdjustedPosition(position, usings);

            // Get completion service
            var completionService = CompletionService.GetService(document);
            if (completionService == null)
            {
                System.Diagnostics.Debug.WriteLine("[Completion] CompletionService is null");
                return new CompletionResult { Items = [] };
            }

            // 确定触发字符
            string? triggerChar = null;
            if (adjustedPosition > 0)
            {
                var text = await document.GetTextAsync(cancellationToken);
                var ch = text[adjustedPosition - 1];
                if (char.IsWhiteSpace(ch) || ch == '.' || ch == '(' || ch == '[' || ch == '<')
                {
                    triggerChar = ch.ToString();
                }
            }

            // Get completions at adjusted position
            var completions = await completionService.GetCompletionsAsync(
                document,
                adjustedPosition,
                trigger: triggerChar != null ? CompletionTrigger.CreateInsertionTrigger(triggerChar[0]) : CompletionTrigger.Invoke,
                cancellationToken: cancellationToken);

            if (completions == null || completions.ItemsList.Count == 0)
            {
                System.Diagnostics.Debug.WriteLine($"[Completion] No completions at position {position}");
                return new CompletionResult { Items = [] };
            }

            // Filter out keywords early if requested
            var meaningfulItems = completions.ItemsList
                .Where(i => !i.Tags.Contains(WellKnownTags.Keyword))
                .ToImmutableArray();

            if (meaningfulItems.Length == 0)
            {
                return new CompletionResult { Items = [] };
            }

            System.Diagnostics.Debug.WriteLine($"[Completion] Found {meaningfulItems.Length} meaningful items");

            // Calculate usings offset for span adjustment
            var usingsOffset = RoslynWorkspaceService.Instance.GetUsingsOffset(usings);

            // 增强和过滤补全项
            var enhancedItems = EnhanceCompletionItems(
                meaningfulItems,
                completionService,
                document,
                usingsOffset,
                cancellationToken);

            // 应用优先级排序
            var sortedItems = ApplyPrioritySort(enhancedItems);

            // 限制返回数量
            var limitedItems = sortedItems.Take(MaxCompletionItems).ToImmutableArray();

            System.Diagnostics.Debug.WriteLine($"[Completion] Returning {limitedItems.Length} enhanced items");

            return new CompletionResult
            {
                Items = limitedItems,
                TriggerCharacter = triggerChar,
                IsIncomplete = sortedItems.Count() > MaxCompletionItems
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
            if (completionService == null) return new CompletionChangeInfo(ImmutableArray<TextChange>.Empty, null, false);

            var change = await completionService.GetChangeAsync(document, item, cancellationToken: cancellationToken);

            // Adjust spans for hidden usings
            var offset = RoslynWorkspaceService.Instance.GetUsingsOffset(usings);

            var adjustedChanges = new List<TextChange>();
            foreach (var textChange in change.TextChanges)
            {
                // If the change is within the hidden usings area, we might need to ignore it or handle carefully.
                // Usually completions happen at the caret, which is after usings.
                // However, imports might be added to the usings area. 
                // For now, if it's in the usings area (Span.Start < offset), we might ignore it or we'd need to handle global usings updates.
                // But the editor doesn't see those lines. 
                // A better approach for "Add using" is checking if it's in the header.

                if (textChange.Span.Start >= offset)
                {
                    var newSpan = new TextSpan(textChange.Span.Start - offset, textChange.Span.Length);
                    adjustedChanges.Add(new TextChange(newSpan, textChange.NewText ?? string.Empty));
                }
            }

            return new CompletionChangeInfo(
                adjustedChanges.ToImmutableArray(),
                change.NewPosition.HasValue ? change.NewPosition.Value - offset : null,
                change.IncludesCommitCharacter);
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

    private List<EnhancedCompletionItem> EnhanceCompletionItems(
        ImmutableArray<CompletionItem> items,
        CompletionService completionService,
        Document document,
        int usingsOffset,
        CancellationToken cancellationToken)
    {
        var enhanced = new List<EnhancedCompletionItem>();

        foreach (var item in items)
        {
            // Adjust span for hidden usings
            var span = item.Span;
            var adjustedSpan = new TextSpan(Math.Max(0, span.Start - usingsOffset), span.Length);

            var enhancedItem = new EnhancedCompletionItem
            {
                RoslynItem = item,
                DisplayText = item.DisplayText,
                SortText = item.SortText,
                FilterText = item.FilterText,
                InlineDescription = item.InlineDescription,
                Tags = item.Tags.ToImmutableArray(),
                Kind = DetermineCompletionKind(item.Tags),
                Priority = CalculateBasePriority(item),
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

    private static int CalculateBasePriority(CompletionItem item)
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

        // 成员优先于类型
        if (tags.Contains(WellKnownTags.Property))
            priority += 2000;
        if (tags.Contains(WellKnownTags.Method))
            priority += 1900;
        if (tags.Contains(WellKnownTags.Field))
            priority += 1800;

        // 类型
        if (tags.Contains(WellKnownTags.Class))
            priority += 1000;
        if (tags.Contains(WellKnownTags.Interface))
            priority += 900;
        if (tags.Contains(WellKnownTags.Structure))
            priority += 800;
        if (tags.Contains(WellKnownTags.Enum))
            priority += 700;

        // 扩展方法稍微降低优先级
        if (tags.Contains(WellKnownTags.ExtensionMethod))
            priority -= 100;

        // 过时的成员降低优先级
        // if (tags.Contains(WellKnownTags.Deprecated))
        //     priority -= 5000;

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

    private static IEnumerable<EnhancedCompletionItem> ApplyPrioritySort(
        List<EnhancedCompletionItem> items)
    {
        // 多级排序:
        // 1. 推荐项优先
        // 2. 按计算的优先级
        // 3. 按排序文本
        return items
            .OrderByDescending(i => i.IsRecommended)
            .ThenByDescending(i => i.Priority)
            .ThenBy(i => i.SortText)
            .ThenBy(i => i.DisplayText);
    }
}
