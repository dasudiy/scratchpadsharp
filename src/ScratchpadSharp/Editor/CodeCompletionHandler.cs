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

public class CodeCompletionHandler
{
    private readonly TextEditor _editor;
    private readonly IRoslynCompletionService _completionService;
    private readonly Func<MainWindowViewModel?> _viewModelProvider;
    private readonly string _tabId;

    private CompletionWindow? _completionWindow;
    private CancellationTokenSource? _completionCts;
    private DateTime _lastCompletionRequest = DateTime.MinValue;
    private DateTime _lastTextChange = DateTime.MinValue;
    private const int CompletionDebounceMs = 150;

    public CodeCompletionHandler(
        TextEditor editor,
        IRoslynCompletionService completionService,
        Func<MainWindowViewModel?> viewModelProvider,
        string tabId)
    {
        _editor = editor;
        _completionService = completionService;
        _viewModelProvider = viewModelProvider;
        _tabId = tabId;
    }

    public void OnTextChanged()
    {
        _lastTextChange = DateTime.UtcNow;
    }

    public void OnTextEntering(TextInputEventArgs e)
    {
        // 如果用户输入的字符会导致当前补全项失效,关闭窗口
        if (_completionWindow != null && e.Text?.Length > 0)
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
        if (_editor == null || e.Text == null) return;

        // 触发条件更智能
        var shouldTrigger = ShouldTriggerCompletion(e.Text);

        if (shouldTrigger)
        {
            _ = ShowCompletionWindowAsync();
        }
    }

    public bool HandleKeyDown(KeyEventArgs e)
    {
        // Ctrl+Space: 手动触发补全
        if (e.Key == Key.Space && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            e.Handled = true;
            _ = ShowCompletionWindowAsync();
            return true;
        }

        // Escape: 关闭所有弹窗
        if (e.Key == Key.Escape)
        {
            if (_completionWindow != null)
            {
                _completionWindow.Close();
                e.Handled = true;
                return true;
            }
        }

        return false;
    }

    private bool ShouldTriggerCompletion(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;

        // 如果窗口已打开,只在特定字符时重新触发
        if (_completionWindow != null)
        {
            return text == "." || text == "<";
        }

        // 点号总是触发
        if (text == ".") return true;

        // 泛型括号
        if (text == "<") return true;

        // 字母、数字、下划线触发(但要确保不是在注释或字符串中)
        if (text.Length == 1)
        {
            var ch = text[0];
            if (char.IsLetterOrDigit(ch) || ch == '_')
            {
                // 简单检查:如果最近没有文本变化,可能是用户刚开始输入
                var timeSinceLastChange = (DateTime.UtcNow - _lastTextChange).TotalMilliseconds;
                return timeSinceLastChange < 2000; // 2秒内的连续输入
            }
        }

        return false;
    }

    private async Task ShowCompletionWindowAsync()
    {
        if (_editor?.TextArea == null) return;

        // Cancel previous completion request
        _completionCts?.Cancel();
        _completionCts = new CancellationTokenSource();
        var token = _completionCts.Token;

        // Debounce
        _lastCompletionRequest = DateTime.UtcNow;
        var requestTime = _lastCompletionRequest;
        await Task.Delay(CompletionDebounceMs, token);

        if (requestTime != _lastCompletionRequest || token.IsCancellationRequested)
            return;

        try
        {
            var code = _editor.Document.Text;
            var offset = _editor.CaretOffset;

            var viewModel = _viewModelProvider();
            var config = viewModel?.CurrentPackage?.Config ?? new ScriptConfig();
            var usings = config.DefaultUsings;
            var packages = config.NuGetPackages;

            // Fetch completions
            var result = await Task.Run(
                () => _completionService.GetCompletionsAsync(_tabId, code, offset, usings, packages, token),
                token);

            if (token.IsCancellationRequested || result.Items.IsEmpty)
                return;

            // Show completion window on UI thread
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (token.IsCancellationRequested) return;

                // Close existing window
                _completionWindow?.Close();

                // Create new completion window
                _completionWindow = new CompletionWindow(_editor.TextArea);
                _completionWindow.Closed += (s, e) => _completionWindow = null;

                // Set window size
                _completionWindow.Width = 650;
                _completionWindow.Height = 450;
                _completionWindow.MinWidth = 450;
                _completionWindow.MinHeight = 250;

                var data = _completionWindow.CompletionList.CompletionData;
                foreach (var item in result.Items)
                {
                    data.Add(new RoslynCompletionData(item, _completionService, _tabId, usings));
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
                        _completionWindow.StartOffset = startOffset;
                    }
                    else
                    {
                        // Fallback to manual word finding if span is invalid (shouldn't happen)
                        // Find start of the word being completed to enable correct filtering
                        var caretOffset = _editor.CaretOffset;
                        var startOffset = caretOffset;
                        while (startOffset > 0)
                        {
                            var ch = code[startOffset - 1];
                            if (!char.IsLetterOrDigit(ch) && ch != '_')
                                break;
                            startOffset--;
                        }
                        _completionWindow.StartOffset = startOffset;
                    }

                    _completionWindow.CompletionList.SelectItem(string.Empty);
                    _completionWindow.Show();
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
