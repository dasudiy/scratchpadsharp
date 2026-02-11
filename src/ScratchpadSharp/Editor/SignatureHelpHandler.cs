using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Editing;
using ScratchpadSharp.Core.Services;
using ScratchpadSharp.Shared.Models;
using ScratchpadSharp.ViewModels;

namespace ScratchpadSharp.Editor;

public class SignatureHelpHandler
{
    private readonly TextEditor _editor;
    private readonly ISignatureProvider _signatureProvider;
    private readonly Func<MainWindowViewModel?> _viewModelProvider;
    private readonly string _tabId;

    private SignatureHelpWindow? _signatureHelpWindow;
    private CancellationTokenSource? _signatureCts;
    private CancellationTokenSource? _caretCheckCts;
    private const int SignatureDebounceMs = 50;

    // Status tracking
    private bool _isSignatureHelpActive = false;
    private int _parenthesisDepth = 0;

    public SignatureHelpHandler(
        TextEditor editor,
        ISignatureProvider signatureProvider,
        Func<MainWindowViewModel?> viewModelProvider,
        string tabId)
    {
        _editor = editor;
        _signatureProvider = signatureProvider;
        _viewModelProvider = viewModelProvider;
        _tabId = tabId;
    }

    public void Initialize()
    {
        if (_editor.TextArea != null)
        {
            _editor.TextArea.Caret.PositionChanged += OnCaretPositionChanged;
        }
    }

    public void HandleInput(TextInputEventArgs e)
    {
        if (_editor == null || e.Text == null || e.Text.Length != 1)
            return;

        var ch = e.Text[0];
        Debug.WriteLine($"[SignatureHelp] Character entered: '{ch}', offset={_editor.CaretOffset}");

        if (ch == '(')
        {
            _parenthesisDepth++;
            _isSignatureHelpActive = true;
            Debug.WriteLine($"[SignatureHelp] Opening paren, depth={_parenthesisDepth}");
            _ = ShowSignatureHelpAsync();
        }
        else if (ch == ')')
        {
            _parenthesisDepth--;
            if (_parenthesisDepth < 0)
                _parenthesisDepth = 0;

            Debug.WriteLine($"[SignatureHelp] Closing paren, depth={_parenthesisDepth}");

            if (_parenthesisDepth == 0)
            {
                _isSignatureHelpActive = false;
                HideSignatureHelp();
            }
            else
            {
                // 可能是嵌套调用,更新签名
                _ = UpdateSignatureHelpAsync();
            }
        }
        else if (ch == ',' && _isSignatureHelpActive)
        {
            Debug.WriteLine($"[SignatureHelp] Comma, updating parameter");
            _ = UpdateSignatureHelpAsync();
        }
        else if (_isSignatureHelpActive && (char.IsLetterOrDigit(ch) || ch == '_' || ch == '.'))
        {
            // 在输入参数时也可以更新
            _ = UpdateSignatureHelpAsync();
        }
    }

    public bool HandleKeyDown(KeyEventArgs e)
    {
        // Escape: 关闭所有弹窗 (Handled by caller mostly, but we can close ours)
        if (e.Key == Key.Escape)
        {
            if (_signatureHelpWindow != null)
            {
                HideSignatureHelp();
                // Caller might want to handle this too to close completion window
                return false;
            }
        }

        // 上下箭头: 导航签名帮助
        if (_signatureHelpWindow?.ViewModel?.IsVisible == true)
        {
            if (e.Key == Key.Up)
            {
                e.Handled = true;
                _signatureHelpWindow.ViewModel.SelectPreviousSignature();
                return true;
            }
            else if (e.Key == Key.Down)
            {
                e.Handled = true;
                _signatureHelpWindow.ViewModel.SelectNextSignature();
                return true;
            }
        }
        return false;
    }

    public void Reset()
    {
        _parenthesisDepth = 0;
        HideSignatureHelp();
    }

    public void HideSignatureHelp()
    {
        _signatureHelpWindow?.Close();
        _signatureHelpWindow = null;
    }

    private async Task ShowSignatureHelpAsync()
    {
        if (_editor?.TextArea == null) return;

        _signatureCts?.Cancel();
        _signatureCts = new CancellationTokenSource();
        var token = _signatureCts.Token;

        // 短暂延迟以避免闪烁
        await Task.Delay(SignatureDebounceMs, token);

        try
        {
            var code = _editor.Document.Text;
            var offset = _editor.CaretOffset;

            Debug.WriteLine($"[SignatureHelp] Code length: {code.Length}, Offset: {offset}");

            var viewModel = _viewModelProvider();
            var config = viewModel?.CurrentPackage?.Config ?? new ScriptConfig();
            var usings = config.DefaultUsings;
            var packages = config.NuGetPackages;

            var (signatures, argIndex, activeParam) = await Task.Run(
                () => _signatureProvider.GetSignaturesAsync(_tabId, code, offset, usings, packages, token),
                token);

            Debug.WriteLine($"[SignatureHelp] Got {signatures.Count} signatures, arg={argIndex}, active={activeParam}");

            if (token.IsCancellationRequested || signatures.Count == 0)
                return;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (token.IsCancellationRequested) return;

                Debug.WriteLine($"[SignatureHelp] Showing popup...");

                _signatureHelpWindow?.Close();

                _signatureHelpWindow = new SignatureHelpWindow(_editor.TextArea);
                _signatureHelpWindow.Closed += (s, e) =>
                {
                    _signatureHelpWindow = null;
                    if (_parenthesisDepth == 0)
                        _isSignatureHelpActive = false;
                };

                _signatureHelpWindow.ViewModel.UpdateSignatures(signatures, activeParam);
                _signatureHelpWindow.ViewModel.Show();

                _signatureHelpWindow.Show();
            });
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Signature help error: {ex.Message}");
        }
    }

    private async Task UpdateSignatureHelpAsync()
    {
        if (_editor?.TextArea == null || _signatureHelpWindow?.ViewModel == null ||
            !_signatureHelpWindow.ViewModel.IsVisible)
            return;

        _signatureCts?.Cancel();
        _signatureCts = new CancellationTokenSource();
        var token = _signatureCts.Token;

        await Task.Delay(SignatureDebounceMs, token);

        try
        {
            var code = _editor.Document.Text;
            var offset = _editor.CaretOffset;

            var viewModel = _viewModelProvider();
            var config = viewModel?.CurrentPackage?.Config ?? new ScriptConfig();
            var usings = config.DefaultUsings;
            var packages = config.NuGetPackages;

            var (_, argIndex, activeParam) = await Task.Run(
                () => _signatureProvider.GetSignaturesAsync(_tabId, code, offset, usings, packages, token),
                token);

            if (!token.IsCancellationRequested && activeParam >= 0)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    _signatureHelpWindow?.ViewModel?.UpdateArgumentIndex(activeParam);
                });
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Signature help update error: {ex.Message}");
        }
    }

    private void OnCaretPositionChanged(object? sender, EventArgs e)
    {
        // 只在签名帮助激活时检查
        if (!_isSignatureHelpActive || _signatureHelpWindow?.ViewModel == null ||
            !_signatureHelpWindow.ViewModel.IsVisible)
            return;

        // Cancel previous check
        _caretCheckCts?.Cancel();
        _caretCheckCts = new CancellationTokenSource();
        var token = _caretCheckCts.Token;

        // Smart detection to close signature help
        if (_editor != null)
        {
            // Debounce
            _ = CheckSignatureHelpVisibilityAsync(token);
        }
    }

    private async Task CheckSignatureHelpVisibilityAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(200, token); // 200ms debounce
            if (token.IsCancellationRequested) return;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (token.IsCancellationRequested) return;

                if (ShouldHideSignatureHelp())
                {
                    HideSignatureHelp();
                    _isSignatureHelpActive = false;
                    _parenthesisDepth = 0;
                }
            });
        }
        catch (OperationCanceledException)
        {
            // Ignored
        }
    }

    private bool ShouldHideSignatureHelp()
    {
        if (_editor == null) return true;

        var offset = _editor.CaretOffset;
        var document = _editor.Document;
        var textLength = document.TextLength;

        // 检查附近是否有括号
        int checkRange = 50;
        int start = Math.Max(0, offset - checkRange);
        int end = Math.Min(textLength, offset + checkRange);
        int length = end - start;

        // 优化：只获取必要的文本片段，而不是整个文档
        var nearbyText = document.GetText(start, length);

        // 计算括号平衡
        int balance = 0;
        bool foundOpenParen = false;

        for (int i = 0; i < nearbyText.Length; i++)
        {
            if (nearbyText[i] == '(')
            {
                balance++;
                foundOpenParen = true;
            }
            else if (nearbyText[i] == ')')
            {
                balance--;
            }
        }

        // 如果没有找到开括号或括号已平衡,隐藏
        return !foundOpenParen || balance <= 0;
    }
}
