using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Highlighting.Xshd;
using ScratchpadSharp.Core.Services;
using ScratchpadSharp.Shared.Models;
using ScratchpadSharp.ViewModels;

namespace ScratchpadSharp.Views;

public partial class MainWindow : Window
{
    private TextEditor? codeEditor;
    private MainWindowViewModel? viewModel;
    private CompletionWindow? completionWindow;
    private readonly IRoslynCompletionService completionService;
    private CancellationTokenSource? completionCts;
    private DateTime lastCompletionRequest = DateTime.MinValue;
    private const int DebounceMilliseconds = 150;

    public MainWindow()
    {
        completionService = new RoslynCompletionService();
        
        InitializeComponent();
        
        codeEditor = this.FindControl<TextEditor>("CodeEditor");
        
        if (codeEditor != null)
        {
            InitializeSyntaxHighlighting();
            InitializeCodeCompletion();
            
            codeEditor.Document = new TextDocument();
            codeEditor.TextChanged += (s, e) =>
            {
                if (viewModel != null)
                {
                    viewModel.CodeText = codeEditor.Document.Text;
                }
            };
            
            // Add Ctrl+Wheel zoom functionality
            codeEditor.PointerWheelChanged += (s, e) =>
            {
                if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
                {
                    var delta = e.Delta.Y;
                    var currentSize = codeEditor.FontSize;
                    var newSize = currentSize + (delta > 0 ? 2 : -2);
                    
                    // Clamp between 8 and 48
                    newSize = Math.Max(8, Math.Min(48, newSize));
                    codeEditor.FontSize = newSize;
                    
                    e.Handled = true;
                }
            };
        }
        
        // Focus editor when window opens
        this.Opened += (s, e) =>
        {
            codeEditor?.Focus();
        };
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        
        viewModel = DataContext as MainWindowViewModel;
        
        if (viewModel != null && codeEditor != null)
        {
            viewModel.PropertyChanged += (s, args) =>
            {
                if (args.PropertyName == nameof(MainWindowViewModel.CodeText) && 
                    codeEditor.Document.Text != viewModel.CodeText)
                {
                    codeEditor.Document.Text = viewModel.CodeText;
                }
            };
            
            codeEditor.Document.Text = viewModel.CodeText;
        }
    }

    private void InitializeCodeCompletion()
    {
        if (codeEditor?.TextArea == null) return;

        // Trigger completion on TextEntering
        codeEditor.TextArea.TextEntering += OnTextEntering;
        codeEditor.TextArea.TextEntered += OnTextEntered;
        
        // Add Ctrl+Space shortcut for manual completion
        codeEditor.TextArea.KeyDown += (s, e) =>
        {
            if (e.Key == Key.Space && e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                e.Handled = true;
                _ = ShowCompletionWindowAsync();
            }
        };
    }

    private void OnTextEntering(object? sender, TextInputEventArgs e)
    {
        // If completion window is open and user types, let it handle filtering
        if (completionWindow != null && e.Text?.Length > 0)
        {
            if (!char.IsLetterOrDigit(e.Text[0]) && e.Text[0] != '_')
            {
                // Commit completion if user types non-identifier character
                completionWindow.CompletionList.RequestInsertion(e);
            }
        }
    }

    private void OnTextEntered(object? sender, TextInputEventArgs e)
    {
        if (codeEditor == null || e.Text == null) return;

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
        if (codeEditor?.TextArea == null) return;

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
            var code = codeEditor.Document.Text;
            var offset = codeEditor.CaretOffset;
            
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
                completionWindow = new CompletionWindow(codeEditor.TextArea);
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
        if (codeEditor == null) return;

        try
        {
            var uri = new Uri("avares://ScratchpadSharp/Assets/CSharp-Mode.xshd");
            using var stream = AssetLoader.Open(uri);
            using var reader = XmlReader.Create(stream);
            var highlighting = HighlightingLoader.Load(reader, HighlightingManager.Instance);
            codeEditor.SyntaxHighlighting = highlighting;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to load custom syntax highlighting: {ex.Message}");
            
            var builtInHighlighting = HighlightingManager.Instance.GetDefinition("C#");
            if (builtInHighlighting != null)
            {
                codeEditor.SyntaxHighlighting = builtInHighlighting;
            }
            else
            {
                Debug.WriteLine("Failed to load built-in C# syntax highlighting");
            }
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
