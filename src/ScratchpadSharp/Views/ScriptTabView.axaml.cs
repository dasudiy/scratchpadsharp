using System;
using System.Diagnostics;
using System.Xml;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Highlighting.Xshd;
using ScratchpadSharp.Core.Services;
using ScratchpadSharp.Editor;
using ScratchpadSharp.ViewModels;

namespace ScratchpadSharp.Views;

public partial class ScriptTabView : UserControl
{
    private ScriptTabViewModel? viewModel;
    private readonly IRoslynCompletionService completionService = new RoslynCompletionService();
    private readonly ISignatureProvider signatureProvider = new SignatureProvider();

    private CodeCompletionHandler? _codeCompletionHandler;
    private SignatureHelpHandler? _signatureHelpHandler;
    private bool _isEditorInitialized;

    public ScriptTabView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
        AttachedToVisualTree += OnAttachedToVisualTree;
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e) =>
        TryInitializeEditor();

    private void OnLoaded(object? sender, RoutedEventArgs e) =>
        TryInitializeEditor();

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (viewModel != null)
            viewModel.PropertyChanged -= OnViewModelPropertyChanged;

        viewModel = DataContext as ScriptTabViewModel;

        if (viewModel != null)
            viewModel.PropertyChanged += OnViewModelPropertyChanged;

        _isEditorInitialized = false;
        _codeCompletionHandler = null;
        _signatureHelpHandler = null;

        TryInitializeEditor();
    }

    private void TryInitializeEditor()
    {
        if (_isEditorInitialized || viewModel == null || CodeEditor == null)
            return;

        if (!IsLoaded)
        {
            Dispatcher.UIThread.Post(TryInitializeEditor, DispatcherPriority.Loaded);
            return;
        }

        InitializeEditor();
    }

    private void InitializeEditor()
    {
        if (_isEditorInitialized || viewModel == null || CodeEditor == null)
            return;

        _isEditorInitialized = true;

        _codeCompletionHandler = new CodeCompletionHandler(
            CodeEditor,
            completionService,
            () => viewModel,
            viewModel.TabId);

        _signatureHelpHandler = new SignatureHelpHandler(
            CodeEditor,
            signatureProvider,
            () => viewModel,
            viewModel.TabId,
            () => _codeCompletionHandler?.ActiveCompletionWindow);

        _codeCompletionHandler.SetCompletionWindowChangedCallback(
            () => _signatureHelpHandler?.UpdatePosition());

        InitializeSyntaxHighlighting();
        InitializeCodeCompletion();
        ApplyEditorChrome();

        CodeEditor.Document ??= new TextDocument();
        CodeEditor.TextChanged += OnCodeEditorTextChanged;
        CodeEditor.PointerWheelChanged += OnPointerWheelChanged;
        CodeEditor.TextArea.Caret.PositionChanged += OnCaretPositionChanged;

        CodeEditor.Document.Text = viewModel.CodeText;
        UpdateOutputPanelLayout(viewModel.IsOutputPanelExpanded);
        UpdateCursorPosition();
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (viewModel == null || CodeEditor == null) return;

        if (e.PropertyName == nameof(ScriptTabViewModel.CodeText) &&
            CodeEditor.Document.Text != viewModel.CodeText)
        {
            CodeEditor.Document.Text = viewModel.CodeText;
            _signatureHelpHandler?.Reset();
        }

        if (e.PropertyName == nameof(ScriptTabViewModel.IsOutputPanelExpanded))
            UpdateOutputPanelLayout(viewModel.IsOutputPanelExpanded);
    }

    private void OnCaretPositionChanged(object? sender, EventArgs e) => UpdateCursorPosition();

    private void UpdateCursorPosition()
    {
        if (viewModel == null || CodeEditor?.TextArea == null) return;

        var line = CodeEditor.TextArea.Caret.Line + 1;
        var column = CodeEditor.TextArea.Caret.Column + 1;
        viewModel.CursorPosition = $"{line}:{column}";
    }

    private void UpdateOutputPanelLayout(bool expanded)
    {
        if (MainGrid == null || MainGrid.RowDefinitions.Count < 3) return;

        MainGrid.RowDefinitions[1].Height = expanded ? GridLength.Auto : new GridLength(0);
        MainGrid.RowDefinitions[2].Height = expanded
            ? new GridLength(2, GridUnitType.Star)
            : GridLength.Auto;
    }

    private void OnCodeEditorTextChanged(object? sender, EventArgs e)
    {
        if (viewModel != null && CodeEditor != null)
            viewModel.CodeText = CodeEditor.Document.Text;

        _codeCompletionHandler?.OnTextChanged();
    }

    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (CodeEditor == null) return;

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            var delta = e.Delta.Y;
            var newSize = Math.Max(8, Math.Min(48, CodeEditor.FontSize + (delta > 0 ? 2 : -2)));
            CodeEditor.FontSize = newSize;
            e.Handled = true;
        }
    }

    private void InitializeCodeCompletion()
    {
        if (CodeEditor?.TextArea == null) return;

        CodeEditor.TextArea.TextEntered += OnTextEntered;
        CodeEditor.TextArea.TextEntering += OnTextEntering;
        CodeEditor.TextArea.KeyDown += OnEditorKeyDown;
        _signatureHelpHandler?.Initialize();
    }

    private void OnTextEntering(object? sender, TextInputEventArgs e) =>
        _codeCompletionHandler?.OnTextEntering(e);

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

    private void ApplyEditorChrome()
    {
        if (CodeEditor?.TextArea == null) return;

        CodeEditor.TextArea.Caret.CaretBrush = Avalonia.Media.Brush.Parse("#307FFF");
        CodeEditor.Options.HighlightCurrentLine = true;
        CodeEditor.TextArea.SelectionBrush = Avalonia.Media.Brush.Parse("#A6D2FF");
    }

    private void InitializeSyntaxHighlighting()
    {
        if (CodeEditor == null) return;

        try
        {
            var uri = new Uri("avares://ScratchpadSharp/Assets/CSharp-Mode.xshd");
            using var stream = AssetLoader.Open(uri);
            using var reader = XmlReader.Create(stream);
            CodeEditor.SyntaxHighlighting = HighlightingLoader.Load(reader, HighlightingManager.Instance);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to load custom syntax highlighting: {ex.Message}");
            CodeEditor.SyntaxHighlighting = HighlightingManager.Instance.GetDefinition("C#");
        }
    }
}
