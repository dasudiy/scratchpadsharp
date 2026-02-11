using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Media;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using ScratchpadSharp.Core.Services;
using ScratchpadSharp.Views;
using ScratchpadSharp.Editor;

namespace ScratchpadSharp.ViewModels;

public class RoslynCompletionData : ICompletionData
{
    private readonly EnhancedCompletionItem enhancedItem;
    private object? content;
    private object? description;

    private readonly IRoslynCompletionService completionService;
    private readonly string tabId;
    private readonly List<string> usings;

    public RoslynCompletionData(
        EnhancedCompletionItem item,
        IRoslynCompletionService completionService,
        string tabId,
        List<string> usings)
    {
        this.enhancedItem = item;
        this.completionService = completionService;
        this.tabId = tabId;
        this.usings = usings;
        Text = item.DisplayText;
    }

    public IImage? Image => IconData.GetIconForTags(enhancedItem.Tags);

    public string Text { get; }

    public object Content
    {
        get
        {
            if (content != null)
                return content;

            var panel = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal };

            // 主文本
            var mainText = new TextBlock
            {
                Text = enhancedItem.DisplayText,
                FontWeight = enhancedItem.IsRecommended ? FontWeight.Bold : FontWeight.Normal,
                Foreground = enhancedItem.IsRecommended
                    ? new SolidColorBrush(Color.FromRgb(0, 102, 204))
                    : Brushes.Black
            };
            panel.Children.Add(mainText);

            // 内联描述
            if (!string.IsNullOrEmpty(enhancedItem.InlineDescription))
            {
                var inlineDesc = new TextBlock
                {
                    Text = $" ({enhancedItem.InlineDescription})",
                    Foreground = Brushes.Gray,
                    Margin = new Avalonia.Thickness(4, 0, 0, 0)
                };
                panel.Children.Add(inlineDesc);
            }

            // 推荐标记
            if (enhancedItem.IsRecommended)
            {
                var star = new TextBlock
                {
                    Text = " ★",
                    Foreground = new SolidColorBrush(Color.FromRgb(255, 193, 7)),
                    Margin = new Avalonia.Thickness(4, 0, 0, 0)
                };
                panel.Children.Add(star);
            }

            content = panel;
            return content;
        }
    }

    public object Description
    {
        get
        {
            if (description != null)
                return description;

            var panel = new StackPanel { MaxWidth = 400 };

            // 类型信息
            if (!string.IsNullOrEmpty(enhancedItem.InlineDescription))
            {
                var typeInfo = new TextBlock
                {
                    Text = GetKindDisplayName(enhancedItem.Kind),
                    FontWeight = FontWeight.Bold,
                    Foreground = new SolidColorBrush(Color.FromRgb(0, 122, 204)),
                    Margin = new Avalonia.Thickness(0, 0, 0, 4)
                };
                panel.Children.Add(typeInfo);
            }

            // 完整签名
            var signature = new TextBlock
            {
                Text = enhancedItem.DisplayText,
                FontFamily = new FontFamily("Cascadia Code, Consolas, monospace"),
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                Margin = new Avalonia.Thickness(0, 0, 0, 8)
            };
            panel.Children.Add(signature);

            // 文档
            if (!string.IsNullOrEmpty(enhancedItem.Documentation))
            {
                var separator = new Border
                {
                    Height = 1,
                    Background = new SolidColorBrush(Color.FromRgb(224, 224, 224)),
                    Margin = new Avalonia.Thickness(0, 0, 0, 8)
                };
                panel.Children.Add(separator);

                var docText = new TextBlock
                {
                    Text = enhancedItem.Documentation,
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                    Foreground = new SolidColorBrush(Color.FromRgb(80, 80, 80))
                };
                panel.Children.Add(docText);
            }

            description = panel;
            return description;
        }
    }

    public double Priority => enhancedItem.Priority;

    public async void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs)
    {
        try
        {
            // Use the service to get the actual change (handles snippets, overrides, etc.)
            var change = await completionService.GetCompletionChangeAsync(
                tabId,
                textArea.Document.Text,
                enhancedItem.RoslynItem,
                usings);

            var document = textArea.Document;

            using (document.RunUpdate())
            {
                // If we have text changes from Roslyn, apply them
                if (change.TextChanges.Length > 0)
                {
                    // Apply changes in reverse order to maintain offsets
                    // Note: Roslyn usually returns them in a way that index order matters, 
                    // but standard practice for multiple changes is reverse if they affect same doc.
                    // However, Roslyn's TextChange is usually calculated against original text.
                    // So we should apply them carefully.
                    // For completion, there is usually just one change or a few.

                    // We need to order by start position descending to apply safely
                    var changes = change.TextChanges.OrderByDescending(c => c.Span.Start).ToList();

                    foreach (var textChange in changes)
                    {
                        var offset = textChange.Span.Start;
                        var length = textChange.Span.Length;
                        var newText = textChange.NewText ?? "";

                        // Check if this text change is likely the main completion replacement
                        // (it overlaps with the completion segment)
                        if (offset <= completionSegment.EndOffset && (offset + length) >= completionSegment.Offset)
                        {
                            // Ensure we consume the entire user-typed segment
                            // This fixes the "Consoleons" bug where Roslyn returns a change for "C" 
                            // but the user has already typed "Cons"
                            var changeEnd = offset + length;
                            if (completionSegment.EndOffset > changeEnd)
                            {
                                length += (completionSegment.EndOffset - changeEnd);
                            }
                        }

                        // Current document length check
                        if (offset >= 0 && offset + length <= document.TextLength)
                        {
                            document.Replace(offset, length, newText);
                        }
                    }
                }
                else
                {
                    // Fallback to simple replacement if no changes returned (unlikely)
                    // Find the word start before the completion segment
                    var startOffset = completionSegment.Offset;

                    // Look back to find the start of the word
                    while (startOffset > 0)
                    {
                        var charBefore = document.GetCharAt(startOffset - 1);
                        if (!char.IsLetterOrDigit(charBefore) && charBefore != '_')
                            break;
                        startOffset--;
                    }

                    // Create extended segment that includes the typed prefix
                    var length = completionSegment.EndOffset - startOffset;

                    // Replace the entire word with the completion text
                    document.Replace(startOffset, length, Text);
                }
            }

            // Move caret if specified
            if (change.NewPosition.HasValue)
            {
                textArea.Caret.Offset = change.NewPosition.Value;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CompletionData] Error applying completion: {ex.Message}");
        }
    }

    private class SimpleSegment : ISegment
    {
        public int Offset { get; }
        public int Length { get; }
        public int EndOffset => Offset + Length;

        public SimpleSegment(int offset, int length)
        {
            Offset = offset;
            Length = length;
        }
    }

    private static string GetKindDisplayName(CompletionItemKind kind)
    {
        return kind switch
        {
            CompletionItemKind.Class => "class",
            CompletionItemKind.Struct => "struct",
            CompletionItemKind.Interface => "interface",
            CompletionItemKind.Enum => "enum",
            CompletionItemKind.Delegate => "delegate",
            CompletionItemKind.Method => "method",
            CompletionItemKind.Property => "property",
            CompletionItemKind.Field => "field",
            CompletionItemKind.Event => "event",
            CompletionItemKind.Constant => "constant",
            CompletionItemKind.Variable => "variable",
            CompletionItemKind.Parameter => "parameter",
            CompletionItemKind.Keyword => "keyword",
            CompletionItemKind.Snippet => "snippet",
            CompletionItemKind.Namespace => "namespace",
            CompletionItemKind.Module => "module",
            CompletionItemKind.Constructor => "constructor",
            CompletionItemKind.ExtensionMethod => "extension method",
            CompletionItemKind.EnumMember => "enum member",
            CompletionItemKind.TypeParameter => "type parameter",
            _ => "item"
        };
    }
}
