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
using Avalonia.Media;
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
using ScratchpadSharp.Editor;

namespace ScratchpadSharp.Views;

public partial class MainWindow : Window
{
    private const string TabId = "main";
    private MainWindowViewModel? viewModel;
    private readonly IRoslynCompletionService completionService;
    private readonly ISignatureProvider signatureProvider;

    private CodeCompletionHandler _codeCompletionHandler = null!;
    private SignatureHelpHandler _signatureHelpHandler = null!;




    public MainWindow()
    {
        completionService = new RoslynCompletionService();
        signatureProvider = new SignatureProvider();

        InitializeComponent();

        if (CodeEditor != null)
        {
            _codeCompletionHandler = new CodeCompletionHandler(
                CodeEditor,
                completionService,
                () => viewModel,
                TabId);

            _signatureHelpHandler = new SignatureHelpHandler(
                CodeEditor,
                signatureProvider,
                () => viewModel,
                TabId);

            InitializeSyntaxHighlighting();
            InitializeCodeCompletion();

            CodeEditor.Document = new TextDocument();
            CodeEditor.TextChanged += OnCodeEditorTextChanged;

            // Add Ctrl+Wheel zoom functionality
            CodeEditor.PointerWheelChanged += OnPointerWheelChanged;
        }

        // Focus editor when window opens
        this.Opened += OnWindowOpened;
        this.Closing += OnWindowClosing;
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
                    _signatureHelpHandler?.Reset();
                }
            };

            CodeEditor.Document.Text = viewModel.CodeText;
        }
    }

    private void OnCodeEditorTextChanged(object? sender, EventArgs e)
    {
        if (viewModel != null && CodeEditor != null)
        {
            viewModel.CodeText = CodeEditor.Document.Text;
        }

        _codeCompletionHandler?.OnTextChanged();
    }

    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (CodeEditor == null) return;

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
    }

    private async void OnWindowOpened(object? sender, EventArgs e)
    {
        // Wait for workspace initialization and create project
        await Task.Run(async () =>
        {
            while (!RoslynWorkspaceService.Instance.IsInitialized)
            {
                await Task.Delay(100);
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                try
                {
                    RoslynWorkspaceService.Instance.CreateProject(TabId);
                    Debug.WriteLine($"[MainWindow] Created Roslyn project for tab '{TabId}'");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[MainWindow] Error creating project: {ex.Message}");
                }
            });
        });

        CodeEditor?.Focus();
    }

    private void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        RoslynWorkspaceService.Instance.RemoveProject(TabId);
        Debug.WriteLine($"[MainWindow] Removed Roslyn project for tab '{TabId}'");
    }

    private void InitializeCodeCompletion()
    {
        if (CodeEditor?.TextArea == null) return;

        // Trigger completion on TextEntered
        CodeEditor.TextArea.TextEntered += OnTextEntered;
        CodeEditor.TextArea.TextEntering += OnTextEntering;

        // Add keyboard shortcuts
        CodeEditor.TextArea.KeyDown += OnEditorKeyDown;

        // Initialize signature help handler
        _signatureHelpHandler.Initialize();
    }

    private void OnTextEntering(object? sender, TextInputEventArgs e)
    {
        _codeCompletionHandler?.OnTextEntering(e);
    }

    private void OnTextEntered(object? sender, TextInputEventArgs e)
    {
        if (CodeEditor == null || e.Text == null) return;

        _signatureHelpHandler?.HandleInput(e);
        _codeCompletionHandler?.OnTextEntered(e);
    }

    private void OnEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (_codeCompletionHandler?.HandleKeyDown(e) == true) return;
        if (_signatureHelpHandler?.HandleKeyDown(e) == true) return;
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
        }
    }


}
