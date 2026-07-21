using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;

namespace ScratchpadSharp.Editor;

/// <summary>Draws red wavy underlines for compilation error spans.</summary>
public sealed class CompilationErrorRenderer : IBackgroundRenderer
{
    private readonly List<TextSegment> _segments = new();
    private static readonly IPen ErrorPen = new Pen(new SolidColorBrush(Color.Parse("#E51400")), 0.9);

    public KnownLayer Layer => KnownLayer.Selection;

    public void SetErrors(TextDocument? document, IEnumerable<(int Line, int Column, int EndLine, int EndColumn)> errors)
    {
        _segments.Clear();
        if (document == null)
            return;

        foreach (var error in errors)
        {
            if (error.Line < 1 || error.Line > document.LineCount)
                continue;

            var startLine = document.GetLineByNumber(error.Line);
            var startOffset = startLine.Offset + Math.Clamp(error.Column - 1, 0, startLine.Length);

            int endOffset;
            if (error.EndLine < 1 || error.EndLine > document.LineCount)
            {
                endOffset = startLine.EndOffset;
            }
            else
            {
                var endLine = document.GetLineByNumber(error.EndLine);
                endOffset = endLine.Offset + Math.Clamp(error.EndColumn - 1, 0, endLine.Length);
            }

            if (endOffset <= startOffset)
                endOffset = Math.Min(startOffset + 1, startLine.EndOffset);

            if (endOffset <= startOffset && startLine.Length == 0)
            {
                // Empty line: mark a small span at the line start for visibility.
                startOffset = startLine.Offset;
                endOffset = startLine.EndOffset;
            }

            if (endOffset > startOffset)
            {
                _segments.Add(new TextSegment
                {
                    StartOffset = startOffset,
                    Length = endOffset - startOffset
                });
            }
        }
    }

    public void Clear() => _segments.Clear();

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (_segments.Count == 0 || textView.Document == null)
            return;

        foreach (var segment in _segments)
        {
            foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, segment))
                DrawWavyUnderline(drawingContext, rect);
        }
    }

    private static void DrawWavyUnderline(DrawingContext drawingContext, Rect rect)
    {
        if (rect.Width < 0.5)
            return;

        const double step = 2.5;
        var y = rect.Bottom - 1;
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(rect.Left, y), false);
            var x = rect.Left;
            var up = true;
            while (x < rect.Right)
            {
                var next = Math.Min(x + step, rect.Right);
                ctx.LineTo(new Point(next, y + (up ? -1.2 : 1.2)));
                x = next;
                up = !up;
            }
        }

        drawingContext.DrawGeometry(null, ErrorPen, geometry);
    }
}
