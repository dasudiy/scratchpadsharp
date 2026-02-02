using System;
using Avalonia.Controls;
using Avalonia.Media;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using Microsoft.CodeAnalysis.Completion;

namespace ScratchpadSharp.Views;

public class RoslynCompletionData : ICompletionData
{
    private readonly CompletionItem completionItem;

    public RoslynCompletionData(CompletionItem completionItem)
    {
        this.completionItem = completionItem;
        Text = completionItem.DisplayText;
        // Create TextBlock with larger font size
        var textBlock = new TextBlock
        {
            Text = GetDisplayContent(completionItem),
        };
        Content = textBlock;
        Description = completionItem.InlineDescription ?? string.Empty;
    }

    public IImage? Image => IconData.GetIconForTags(completionItem.Tags);

    public string Text { get; }

    public object Content { get; }

    public object Description { get; }

    public double Priority => completionItem.Rules.MatchPriority;

    public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs)
    {
        // Find the word start before the completion segment
        var document = textArea.Document;
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
        var extendedSegment = new SimpleSegment(startOffset, length);

        // Replace the entire word with the completion text
        document.Replace(extendedSegment, Text);
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

    private static string GetDisplayContent(CompletionItem item)
    {
        var displayText = item.DisplayText;

        // Show inline description if available
        if (!string.IsNullOrEmpty(item.InlineDescription))
        {
            return $"{displayText} ({item.InlineDescription})";
        }
        return displayText;
    }
}
