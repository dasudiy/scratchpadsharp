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

            var mainText = new TextBlock
            {
                Text = enhancedItem.DisplayText,
                FontSize = EditorPopupTheme.ItemFontSize,
                FontFamily = EditorPopupTheme.CodeFont,
                FontWeight = enhancedItem.IsRecommended ? FontWeight.SemiBold : FontWeight.Normal,
                Foreground = enhancedItem.IsRecommended
                    ? EditorPopupTheme.Accent
                    : EditorPopupTheme.TextPrimary
            };
            panel.Children.Add(mainText);

            if (!string.IsNullOrEmpty(enhancedItem.InlineDescription))
            {
                panel.Children.Add(new TextBlock
                {
                    Text = $" — {enhancedItem.InlineDescription}",
                    FontSize = EditorPopupTheme.MetaFontSize,
                    Foreground = EditorPopupTheme.TextMuted,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
            }

            if (enhancedItem.IsRecommended)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = " ★",
                    FontSize = EditorPopupTheme.MetaFontSize,
                    Foreground = EditorPopupTheme.Warning,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                });
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

            var panel = new StackPanel { MaxWidth = EditorPopupTheme.DescriptionMaxWidth };

            var typeInfo = new TextBlock
            {
                Text = GetKindDisplayName(enhancedItem.Kind),
                FontSize = EditorPopupTheme.MetaFontSize,
                FontWeight = FontWeight.SemiBold,
                Foreground = EditorPopupTheme.Accent,
                Margin = new Avalonia.Thickness(0, 0, 0, 4)
            };
            panel.Children.Add(typeInfo);

            var signature = new TextBlock
            {
                Text = enhancedItem.DisplayText,
                FontFamily = EditorPopupTheme.CodeFont,
                FontSize = EditorPopupTheme.CodeFontSize,
                Foreground = EditorPopupTheme.TextPrimary,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                Margin = new Avalonia.Thickness(0, 0, 0, 8)
            };
            panel.Children.Add(signature);

            if (!string.IsNullOrEmpty(enhancedItem.Documentation))
            {
                AddDocumentation(panel, enhancedItem.Documentation);
            }
            else
            {
                var loadingText = new TextBlock
                {
                    Text = "Loading documentation...",
                    FontStyle = FontStyle.Italic,
                    FontSize = EditorPopupTheme.MetaFontSize,
                    Foreground = EditorPopupTheme.TextMuted,
                    Margin = new Avalonia.Thickness(0, 0, 0, 8)
                };
                panel.Children.Add(loadingText);
                LoadDescriptionAsync(panel, loadingText);
            }

            description = panel;
            return description;
        }
    }

    private static void AddDocumentation(StackPanel panel, string doc)
    {
        panel.Children.Add(new Border
        {
            Height = 1,
            Background = EditorPopupTheme.Border,
            Margin = new Avalonia.Thickness(0, 0, 0, 8)
        });

        panel.Children.Add(new TextBlock
        {
            Text = doc,
            FontSize = EditorPopupTheme.MetaFontSize,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Foreground = EditorPopupTheme.TextSecondary
        });
    }

    private async void LoadDescriptionAsync(StackPanel panel, TextBlock loadingPlaceholder)
    {
        try
        {
            var doc = await completionService.GetCompletionDescriptionAsync(tabId, enhancedItem.RoslynItem);

            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                panel.Children.Remove(loadingPlaceholder);

                if (!string.IsNullOrEmpty(doc))
                {
                    enhancedItem.Documentation = doc;
                    AddDocumentation(panel, doc);
                }
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CompletionData] Error loading description: {ex.Message}");
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                panel.Children.Remove(loadingPlaceholder);
            });
        }
    }


    public double Priority => enhancedItem.Priority;

    public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs)
    {
        try
        {
            // AvaloniaEdit expects synchronous insertion when the user accepts a completion item.
            var change = completionService.GetCompletionChangeAsync(
                tabId,
                textArea.Document.Text,
                enhancedItem.RoslynItem,
                usings).GetAwaiter().GetResult();

            var document = textArea.Document;

            using (document.RunUpdate())
            {
                // Step 1: Apply code-area text changes FIRST (offsets are valid before any top-insertion)
                if (change.TextChanges.Length > 0)
                {
                    var changes = change.TextChanges.OrderByDescending(c => c.Span.Start).ToList();

                    foreach (var textChange in changes)
                    {
                        var offset = textChange.Span.Start;
                        var length = textChange.Span.Length;
                        var newText = textChange.NewText ?? "";

                        // Extend to cover the full user-typed segment if needed
                        if (offset <= completionSegment.EndOffset && (offset + length) >= completionSegment.Offset)
                        {
                            var changeEnd = offset + length;
                            if (completionSegment.EndOffset > changeEnd)
                                length += (completionSegment.EndOffset - changeEnd);
                        }

                        if (offset >= 0 && offset + length <= document.TextLength)
                            document.Replace(offset, length, newText);
                    }
                }
                else
                {
                    // Fallback to simple replacement
                    var startOffset = completionSegment.Offset;
                    while (startOffset > 0)
                    {
                        var ch = document.GetCharAt(startOffset - 1);
                        if (!char.IsLetterOrDigit(ch) && ch != '_') break;
                        startOffset--;
                    }
                    document.Replace(startOffset, completionSegment.EndOffset - startOffset, Text);
                }

                if (!change.NewUsings.IsEmpty)
                {
                    foreach (var ns in change.NewUsings)
                    {
                        if (!usings.Contains(ns))
                            usings.Add(ns);
                    }
                }
            }

            if (change.NewPosition.HasValue)
                textArea.Caret.Offset = change.NewPosition.Value;
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
