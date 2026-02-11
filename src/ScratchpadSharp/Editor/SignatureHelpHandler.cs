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
    private CancellationTokenSource? _updateCts;
    private const int UpdateDebounceMs = 100;

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
        if (e.Text == "(" || e.Text == ",")
        {
            TriggerUpdate(0);
        }
    }

    public bool HandleKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (_signatureHelpWindow != null)
            {
                HideSignatureHelp();
                return true;
            }
        }

        return false;
    }

    public void Reset()
    {
        HideSignatureHelp();
    }

    public void HideSignatureHelp()
    {
        _updateCts?.Cancel();
        _signatureHelpWindow?.Close();
        _signatureHelpWindow = null;
    }

    private void OnCaretPositionChanged(object? sender, EventArgs e)
    {
        TriggerUpdate(UpdateDebounceMs);
    }

    private void TriggerUpdate(int delayMs)
    {
        _updateCts?.Cancel();
        _updateCts = new CancellationTokenSource();
        var token = _updateCts.Token;

        _ = UpdateOrShowSignatureHelpAsync(token, delayMs);
    }

    private async Task UpdateOrShowSignatureHelpAsync(CancellationToken token, int delayMs)
    {
        try
        {
            if (delayMs > 0)
            {
                await Task.Delay(delayMs, token);
            }

            if (token.IsCancellationRequested || _editor?.TextArea == null) return;

            string code = string.Empty;
            int offset = 0;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_editor.Document != null)
                {
                    code = _editor.Document.Text;
                    offset = _editor.CaretOffset;
                }
            });

            if (token.IsCancellationRequested) return;

            var viewModel = _viewModelProvider();
            var config = viewModel?.CurrentPackage?.Config ?? new ScriptConfig();
            var usings = config.DefaultUsings;
            var packages = config.NuGetPackages;

            var (signatures, argIndex, activeParam) = await Task.Run(
                () => _signatureProvider.GetSignaturesAsync(_tabId, code, offset, usings, packages, token),
                token);

            if (token.IsCancellationRequested) return;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (token.IsCancellationRequested) return;

                if (signatures == null || signatures.Count == 0)
                {
                    if (_signatureHelpWindow != null)
                    {
                        HideSignatureHelp();
                    }
                }
                else
                {
                    if (_signatureHelpWindow == null)
                    {
                        _signatureHelpWindow = new SignatureHelpWindow(_editor.TextArea);
                        _signatureHelpWindow.Closed += (s, args) => _signatureHelpWindow = null;
                        _signatureHelpWindow.Show();
                    }

                    _signatureHelpWindow.ViewModel.UpdateSignatures(signatures, activeParam);

                    // Dispatcher.UIThread.Post(() => _signatureHelpWindow?.UpdatePosition(), DispatcherPriority.Input);
                }
            });
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SignatureHelp] Error: {ex.Message}");
            await Dispatcher.UIThread.InvokeAsync(HideSignatureHelp);
        }
    }
}
