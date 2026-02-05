using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Highlighting.Xshd;
using AvaloniaEdit.Rendering;
using ScratchpadSharp.Core.Services;
using ScratchpadSharp.Shared.Models;
using ScratchpadSharp.ViewModels;

namespace ScratchpadSharp.Views;

public partial class MainWindow : Window
{
    private MainWindowViewModel? viewModel;
    private CompletionWindow? completionWindow;
    private Popup? signaturePopup;
    private SignatureHelpViewModel? signatureViewModel;
    private readonly IRoslynCompletionService completionService;
    private readonly ISignatureProvider signatureProvider;
    private CancellationTokenSource? completionCts;
    private CancellationTokenSource? signatureCts;
    private DateTime lastCompletionRequest = DateTime.MinValue;
    private int parenthesisDepth = 0;
    private const int DebounceMilliseconds = 50;

    public MainWindow()
    {
        completionService = new RoslynCompletionService();
        signatureProvider = new SignatureProvider();

        InitializeComponent();

        if (CodeEditor != null)
        {
            InitializeSyntaxHighlighting();
            InitializeCodeCompletion();
            CreateSignaturePopup();

            CodeEditor.Document = new TextDocument();
            CodeEditor.TextChanged += (s, e) =>
            {
                if (viewModel != null)
                {
                    viewModel.CodeText = CodeEditor.Document.Text;
                }
            };

            // Add Ctrl+Wheel zoom functionality
            CodeEditor.PointerWheelChanged += (s, e) =>
            {
                if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
                {
                    var delta = e.Delta.Y;
                    var currentSize = CodeEditor.FontSize;
                    var newSize = currentSize + (delta > 0 ? 2 : -2);

                    // Clamp between 8 and 48
                    newSize = Math.Max(8, Math.Min(48, newSize));
                    CodeEditor.FontSize = newSize;

                    e.Handled = true;
                }
            };
        }

        // Focus editor when window opens
        this.Opened += (s, e) =>
        {
            CodeEditor?.Focus();
        };
    }

    private void CreateSignaturePopup()
    {
        signatureViewModel = new SignatureHelpViewModel();
        signaturePopup = new Popup
        {
            // PlacementTarget = CodeEditor.TextArea,
            // Placement = PlacementMode.Top,
            IsLightDismissEnabled = true,
            Child = new SignatureHelpPopup { DataContext = signatureViewModel },
        };

        // Add to visual tree
        if (MainGrid != null)
        {
            Console.WriteLine($"[SignatureHelp] Adding popup to visual tree...");
            MainGrid.Children.Add(signaturePopup);
        }

        Debug.WriteLine($"[SignatureHelp] Popup created and added to visual tree");
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        viewModel = DataContext as MainWindowViewModel;

        if (viewModel != null && CodeEditor != null)
        {
            viewModel.PropertyChanged += (s, args) =>
            {
                if (args.PropertyName == nameof(MainWindowViewModel.CodeText) &&
                    CodeEditor.Document.Text != viewModel.CodeText)
                {
                    CodeEditor.Document.Text = viewModel.CodeText;
                    parenthesisDepth = 0;
                    HideSignatureHelp();
                }
            };

            CodeEditor.Document.Text = viewModel.CodeText;
        }
    }

    private void InitializeCodeCompletion()
    {
        if (CodeEditor?.TextArea == null) return;

        // Trigger completion on TextEntered
        CodeEditor.TextArea.TextEntered += OnTextEntered;

        // Add Ctrl+Space shortcut for manual completion
        CodeEditor.TextArea.KeyDown += (s, e) =>
        {
            if (e.Key == Key.Space && e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                e.Handled = true;
                _ = ShowCompletionWindowAsync();
            }
            else if (e.Key == Key.Escape)
            {
                HideSignatureHelp();
            }
            else if (signatureViewModel?.IsVisible == true)
            {
                // Only handle Up/Down when signature help is visible
                if (e.Key == Key.Up)
                {
                    e.Handled = true;
                    signatureViewModel.SelectPreviousSignature();
                }
                else if (e.Key == Key.Down)
                {
                    e.Handled = true;
                    signatureViewModel.SelectNextSignature();
                }
            }
        };

        // Signature Help handlers
        CodeEditor.TextArea.TextEntered += OnTextEnteredSignatureHelp;
        CodeEditor.TextArea.Caret.PositionChanged += OnCaretPositionChanged;
    }

    private void OnTextEntered(object? sender, TextInputEventArgs e)
    {
        if (CodeEditor == null || e.Text == null) return;

        // Don't re-trigger if window is already open and user is typing letters/digits
        // (let the existing window handle filtering)
        if (completionWindow != null && e.Text.Length == 1 &&
            (char.IsLetterOrDigit(e.Text[0]) || e.Text[0] == '_'))
        {
            return;
        }

        // Trigger on '.' or after typing alphanumeric/underscore when window is closed
        var shouldTrigger = e.Text == "." ||
                           (e.Text.Length == 1 && (char.IsLetterOrDigit(e.Text[0]) || e.Text[0] == '_'));

        if (shouldTrigger)
        {
            _ = ShowCompletionWindowAsync();
        }
    }

    private async Task ShowCompletionWindowAsync()
    {
        if (CodeEditor?.TextArea == null) return;

        // Cancel previous completion request
        completionCts?.Cancel();
        completionCts = new CancellationTokenSource();
        var token = completionCts.Token;

        // Debounce: wait a bit to avoid spamming
        lastCompletionRequest = DateTime.UtcNow;
        var requestTime = lastCompletionRequest;
        await Task.Delay(DebounceMilliseconds, token);

        // Check if another request came in during debounce
        if (requestTime != lastCompletionRequest || token.IsCancellationRequested)
            return;

        try
        {
            var code = CodeEditor.Document.Text;
            var offset = CodeEditor.CaretOffset;

            // Get usings and packages from config
            var config = viewModel?.CurrentPackage?.Config ?? new ScriptConfig();
            var usings = config.DefaultUsings;
            var packages = config.NuGetPackages;

            // Fetch completions on background thread
            var completions = await Task.Run(
                () => completionService.GetCompletionsAsync(code, offset, usings, packages, token),
                token);

            if (token.IsCancellationRequested || completions.IsEmpty)
                return;

            // Show completion window on UI thread
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (token.IsCancellationRequested) return;

                // Close existing window
                completionWindow?.Close();

                // Create new completion window
                completionWindow = new CompletionWindow(CodeEditor.TextArea);
                completionWindow.Closed += (s, e) => completionWindow = null;

                // Increase window dimensions
                completionWindow.Width = 600;
                completionWindow.Height = 400;
                completionWindow.MinWidth = 400;
                completionWindow.MinHeight = 200;

                var data = completionWindow.CompletionList.CompletionData;
                foreach (var item in completions)
                {
                    data.Add(new RoslynCompletionData(item));
                }

                if (data.Count > 0)
                {
                    // Enable filtering as user types
                    completionWindow.CompletionList.SelectItem(string.Empty);
                    completionWindow.Show();
                }
            });
        }
        catch (OperationCanceledException)
        {
            // Expected when cancellation occurs
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Completion error: {ex.Message}");
        }
    }

    private void InitializeSyntaxHighlighting()
    {
        if (CodeEditor == null) return;

        try
        {
            var uri = new Uri("avares://ScratchpadSharp/Assets/CSharp-Mode.xshd");
            using var stream = AssetLoader.Open(uri);
            using var reader = XmlReader.Create(stream);
            var highlighting = HighlightingLoader.Load(reader, HighlightingManager.Instance);
            CodeEditor.SyntaxHighlighting = highlighting;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to load custom syntax highlighting: {ex.Message}");

            var builtInHighlighting = HighlightingManager.Instance.GetDefinition("C#");
            if (builtInHighlighting != null)
            {
                CodeEditor.SyntaxHighlighting = builtInHighlighting;
            }
            else
            {
                Debug.WriteLine("Failed to load built-in C# syntax highlighting");
            }
        }
    }

    private void OnTextEnteredSignatureHelp(object? sender, TextInputEventArgs e)
    {
        if (CodeEditor == null || e.Text == null || e.Text.Length != 1)
            return;

        var ch = e.Text[0];
        Debug.WriteLine($"[SignatureHelp] Character entered: '{ch}', code length: {CodeEditor.Document.TextLength}");

        if (ch == '(')
        {
            parenthesisDepth++;
            Debug.WriteLine($"[SignatureHelp] Opening paren, depth={parenthesisDepth}, offset={CodeEditor.CaretOffset}");
            _ = ShowSignatureHelpAsync();
        }
        else if (ch == ')')
        {
            parenthesisDepth--;
            if (parenthesisDepth < 0)
                parenthesisDepth = 0;

            Debug.WriteLine($"[SignatureHelp] Closing paren, depth={parenthesisDepth}");
            if (parenthesisDepth == 0)
            {
                HideSignatureHelp();
            }
        }
        else if (ch == ',' && parenthesisDepth > 0)
        {
            Debug.WriteLine($"[SignatureHelp] Comma, updating parameter index");
            _ = UpdateSignatureHelpAsync();
        }
    }

    private async Task ShowSignatureHelpAsync()
    {
        if (CodeEditor?.TextArea == null) return;

        signatureCts?.Cancel();
        signatureCts = new CancellationTokenSource();
        var token = signatureCts.Token;

        try
        {
            var code = CodeEditor.Document.Text;
            var offset = CodeEditor.CaretOffset;

            Debug.WriteLine($"[SignatureHelp] Code to analyze (length {code.Length}):\n{code}");
            Debug.WriteLine($"[SignatureHelp] Offset: {offset}");

            var config = viewModel?.CurrentPackage?.Config ?? new ScriptConfig();
            var usings = config.DefaultUsings;
            var packages = config.NuGetPackages;

            Debug.WriteLine($"[SignatureHelp] Default usings count: {usings.Count}");

            var (signatures, argIndex) = await Task.Run(
                () => signatureProvider.GetSignaturesAsync(code, offset, usings, packages, token),
                token);

            Debug.WriteLine($"[SignatureHelp] Got {signatures.Count} signatures, argIndex={argIndex}");

            if (token.IsCancellationRequested || signatures.Count == 0)
                return;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (token.IsCancellationRequested) return;

                Debug.WriteLine($"[SignatureHelp] Showing popup...");
                signatureViewModel ??= new SignatureHelpViewModel();
                signatureViewModel.UpdateSignatures(signatures, argIndex);
                Debug.WriteLine($"[SignatureHelp] ViewModel IsVisible before Show: {signatureViewModel.IsVisible}");
                signatureViewModel.Show();
                Debug.WriteLine($"[SignatureHelp] ViewModel IsVisible after Show: {signatureViewModel.IsVisible}");
                Debug.WriteLine($"[SignatureHelp] Signatures count: {signatureViewModel.Signatures.Count}");
                Debug.WriteLine($"[SignatureHelp] Current signature: {signatureViewModel.CurrentSignature?.Name}");

                if (signaturePopup != null && !signaturePopup.IsOpen)
                {
                    Debug.WriteLine($"[SignatureHelp] About to position popup...");
                    PositionSignaturePopup();
                    Debug.WriteLine($"[SignatureHelp] Popup opened: {signaturePopup.IsOpen}");
                }
                else
                {
                    Debug.WriteLine($"[SignatureHelp] Popup is null or already open: null={signaturePopup == null}, isOpen={signaturePopup?.IsOpen}");
                }
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
        if (CodeEditor?.TextArea == null || signatureViewModel == null || !signatureViewModel.IsVisible)
            return;

        signatureCts?.Cancel();
        signatureCts = new CancellationTokenSource();
        var token = signatureCts.Token;

        try
        {
            var code = CodeEditor.Document.Text;
            var offset = CodeEditor.CaretOffset;

            var config = viewModel?.CurrentPackage?.Config ?? new ScriptConfig();
            var usings = config.DefaultUsings;
            var packages = config.NuGetPackages;

            var (_, argIndex) = await Task.Run(
                () => signatureProvider.GetSignaturesAsync(code, offset, usings, packages, token),
                token);

            if (!token.IsCancellationRequested && argIndex >= 0)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    signatureViewModel?.UpdateArgumentIndex(argIndex);
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

    private void PositionSignaturePopup()
    {
        if (signaturePopup == null || CodeEditor?.TextArea == null)
        {
            Debug.WriteLine($"[SignatureHelp] PositionSignaturePopup failed: popup={signaturePopup == null}, editor={CodeEditor == null}");
            return;
        }

        try
        {
            var caretPos = CodeEditor.TextArea.Caret.CalculateCaretRectangle();
            
            // Set offset to position at caret
            signaturePopup.PlacementTarget = CodeEditor;
            signaturePopup.Placement = PlacementMode.TopEdgeAlignedLeft;
            signaturePopup.HorizontalOffset = caretPos.X+50;
            signaturePopup.VerticalOffset = caretPos.Bottom;
            Debug.WriteLine($"[SignatureHelp] Setting offset to ({caretPos.X}, {caretPos.Y})");

            if (!signaturePopup.IsOpen)
            {
                Debug.WriteLine($"[SignatureHelp] Opening popup...");
                signaturePopup.IsOpen = true;
                Debug.WriteLine($"[SignatureHelp] IsOpen is now: {signaturePopup.IsOpen}");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error positioning signature popup: {ex.Message}");
        }
    }

    private void OnCaretPositionChanged(object? sender, EventArgs e)
    {
        if (signatureViewModel == null || !signatureViewModel.IsVisible)
            return;

        // Hide signature help if caret moves outside the invocation context
        if (CodeEditor?.TextArea != null)
        {
            var offset = CodeEditor.CaretOffset;
            var code = CodeEditor.Document.Text;
            
            // Simple check: if we're not near parentheses, hide
            if (offset > 0 && offset <= code.Length)
            {
                var nearbyText = code.Substring(Math.Max(0, offset - 20), Math.Min(40, code.Length - Math.Max(0, offset - 20)));
                if (!nearbyText.Contains('('))
                {
                    HideSignatureHelp();
                }
            }
        }
    }

    private void HideSignatureHelp()
    {
        if (signaturePopup != null && signaturePopup.IsOpen)
        {
            signaturePopup.IsOpen = false;
        }

        if (signatureViewModel != null)
        {
            signatureViewModel.Hide();
        }
    }
}
