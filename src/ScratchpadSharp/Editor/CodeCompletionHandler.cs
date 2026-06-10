using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using ScratchpadSharp.Core.Services;
using ScratchpadSharp.Shared.Models;
using ScratchpadSharp.ViewModels;

namespace ScratchpadSharp.Editor;

public class CodeCompletionHandler(
    TextEditor editor,
    IRoslynCompletionService completionService,
    Func<ScriptTabViewModel?> viewModelProvider,
    string tabId)
{
    private const int CompletionDebounceMs = 150;
    private CompletionWindow? completionWindow;
    private CancellationTokenSource? completionCts;
    private DateTime lastCompletionRequest = DateTime.MinValue;
    private DateTime lastTextChange = DateTime.MinValue;
    private Action? completionWindowChanged;

    public CompletionWindow? ActiveCompletionWindow => completionWindow is { IsOpen: true } ? completionWindow : null;

    public void SetCompletionWindowChangedCallback(Action? callback) =>
        completionWindowChanged = callback;

    public void OnTextChanged()
    {
        lastTextChange = DateTime.UtcNow;
    }

    public void OnTextEntering(TextInputEventArgs e)
    {
        // 如果用户输入的字符会导致当前补全项失效,关闭窗口
        if (completionWindow != null && e.Text?.Length > 0)
        {
            var ch = e.Text[0];
            // 某些字符会提交补全
            if (ch == '.' || ch == '(' || ch == ')' || ch == ';' || ch == '{' || ch == '}')
            {
                // 让补全窗口处理
            }
        }
    }

    public void OnTextEntered(TextInputEventArgs e)
    {
        if (e.Text == null) return;

        lastTextChange = DateTime.UtcNow;

        var shouldTrigger = ShouldTriggerCompletion(e.Text);

        if (shouldTrigger)
        {
            _ = ShowCompletionWindowAsync(manualInvoke: false);
        }
    }

    public bool HandleKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Space && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            e.Handled = true;
            _ = ShowCompletionWindowAsync(manualInvoke: true);
            return true;
        }

        // Escape: 关闭所有弹窗
        if (e.Key == Key.Escape)
        {
            if (completionWindow != null)
            {
                completionWindow.Close();
                e.Handled = true;
                return true;
            }
        }

        // Home/End: 关闭补全窗口，让编辑器自己处理
        if (e.Key == Key.Home || e.Key == Key.End)
        {
            if (completionWindow != null)
            {
                completionWindow.Close();
                // 不 e.Handled = true，让事件继续传递给编辑器
            }
            return false;
        }

        return false;
    }

    private bool ShouldTriggerCompletion(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;

        // 如果窗口已打开,只在特定字符时重新触发
        if (completionWindow != null)
        {
            return text == "." || text == "<";
        }

        // 点号总是触发
        if (text == ".") return true;

        // 泛型括号
        if (text == "<") return true;

        if (text.Length == 1)
        {
            var ch = text[0];
            if (char.IsWhiteSpace(ch))
                return IsInUsingDirectiveContext();

            if (char.IsLetterOrDigit(ch) || ch == '_')
            {
                var timeSinceLastChange = (DateTime.UtcNow - lastTextChange).TotalMilliseconds;
                return timeSinceLastChange < 2000;
            }
        }

        return false;
    }

    private bool IsInUsingDirectiveContext()
    {
        if (editor?.Document == null)
            return false;

        var caretOffset = editor.CaretOffset;
        var line = editor.Document.GetLineByOffset(caretOffset);
        var lineText = editor.Document.GetText(line.Offset, caretOffset - line.Offset);
        return lineText.TrimStart().StartsWith("using ", StringComparison.Ordinal);
    }

    private async Task ShowCompletionWindowAsync(bool manualInvoke)
    {
        if (editor?.TextArea == null) return;

        // Cancel previous completion request
        completionCts?.Cancel();
        completionCts = new CancellationTokenSource();
        var token = completionCts.Token;

        lastCompletionRequest = DateTime.UtcNow;
        var requestTime = lastCompletionRequest;

        if (!manualInvoke)
        {
            await Task.Delay(CompletionDebounceMs, token);
            if (requestTime != lastCompletionRequest || token.IsCancellationRequested)
                return;
        }

        try
        {
            var viewModel = viewModelProvider();
            if (viewModel is not { IsProjectReady: true })
                return;

            var code = editor.Document.Text;
            var offset = editor.CaretOffset;
            var usings = viewModel.ProjectContext.Config.Usings;

            var result = await Task.Run(
                () => completionService.GetCompletionsAsync(
                    tabId, code, offset, viewModel.ProjectContext, manualInvoke, token),
                token);

            if (token.IsCancellationRequested || result.Items.IsEmpty)
                return;

            // Show completion window on UI thread
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (token.IsCancellationRequested) return;

                // Close existing window
                completionWindow?.Close();

                // Create new completion window
                completionWindow = new CompletionWindow(editor.TextArea);
                completionWindow.Closed += (_, _) =>
                {
                    completionWindow = null;
                    completionWindowChanged?.Invoke();
                };
                completionWindow.SizeChanged += (_, _) => completionWindowChanged?.Invoke();

                completionWindow.Width = EditorPopupTheme.ListWidth;
                completionWindow.MaxWidth = EditorPopupTheme.ListWidth;
                completionWindow.MaxHeight = EditorPopupTheme.ListMaxHeight;
                completionWindow.Height = EditorPopupTheme.ListMaxHeight;
                completionWindow.MinWidth = 280;
                completionWindow.MinHeight = 160;

                var data = completionWindow.CompletionList.CompletionData;
                foreach (var item in result.Items)
                {
                    data.Add(new RoslynCompletionData(item, completionService, tabId, usings));
                }

                if (data.Count > 0)
                {
                    // Use the span from the first item to determine the start offset
                    // This ensures the window opens at the correct position as determined by Roslyn
                    var firstItem = result.Items[0];
                    var span = firstItem.CompletionSpan;

                    if (span.Length >= 0)
                    {
                        var startOffset = span.Start;
                        completionWindow.StartOffset = startOffset;
                    }
                    else
                    {
                        // Fallback to manual word finding if span is invalid (shouldn't happen)
                        // Find start of the word being completed to enable correct filtering
                        var caretOffset = editor.CaretOffset;
                        var startOffset = caretOffset;
                        while (startOffset > 0)
                        {
                            var ch = code[startOffset - 1];
                            if (!char.IsLetterOrDigit(ch) && ch != '_')
                                break;
                            startOffset--;
                        }
                        completionWindow.StartOffset = startOffset;
                    }

                    completionWindow.CompletionList.SelectItem(string.Empty);
                    completionWindow.Show();
                    completionWindowChanged?.Invoke();
                    Dispatcher.UIThread.Post(() => completionWindowChanged?.Invoke(), DispatcherPriority.Render);
                }
            });
        }
        catch (OperationCanceledException)
        {
            // Expected
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Completion error: {ex.Message}");
        }
    }
}
