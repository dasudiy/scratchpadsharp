using System;
using System.Diagnostics;
using System.Linq;
using System.Xml;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Highlighting.Xshd;
using AvaloniaEdit.Rendering;
using ScratchpadSharp.Core.Configuration;
using ScratchpadSharp.Core.Services;
using ScratchpadSharp.Editor;
using ScratchpadSharp.Services;
using ScratchpadSharp.ViewModels;

namespace ScratchpadSharp.Views;

public partial class ScriptTabView : UserControl
{
    private ScriptTabViewModel? viewModel;
    private bool suppressRenameCommit;
    private readonly IRoslynCompletionService completionService = new RoslynCompletionService();
    private readonly ISignatureProvider signatureProvider = new SignatureProvider();

    private CodeCompletionHandler? _codeCompletionHandler;
    private SignatureHelpHandler? _signatureHelpHandler;
    private CompilationErrorRenderer? _errorRenderer;
    private bool _isEditorInitialized;

    public ScriptTabView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
        SizeChanged += OnViewSizeChanged;
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
        HookOutputWebView();
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        ApplicationSettings.Changed += OnApplicationSettingsChanged;
        TryInitializeEditor();
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        ApplicationSettings.Changed -= OnApplicationSettingsChanged;
        DetachEditorHooks();
        _isEditorInitialized = false;
    }

    private void OnApplicationSettingsChanged() =>
        Dispatcher.UIThread.Post(ApplyEditorSettings);

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        TryInitializeEditor();
        LoadOutputDocument();
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (viewModel != null)
        {
            viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            viewModel.RenameEditStarted -= OnRenameEditStarted;
            viewModel.DumpFragmentAppended -= OnDumpFragmentAppended;
            viewModel.DumpHtmlCleared -= OnDumpHtmlCleared;
        }

        viewModel = DataContext as ScriptTabViewModel;

        if (viewModel != null)
        {
            viewModel.PropertyChanged += OnViewModelPropertyChanged;
            viewModel.RenameEditStarted += OnRenameEditStarted;
            viewModel.DumpFragmentAppended += OnDumpFragmentAppended;
            viewModel.DumpHtmlCleared += OnDumpHtmlCleared;
        }

        DetachEditorHooks();
        _isEditorInitialized = false;
        _codeCompletionHandler = null;
        _signatureHelpHandler = null;
        _errorRenderer = null;

        TryInitializeEditor();
        LoadOutputDocument();
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

    private void DetachEditorHooks()
    {
        if (CodeEditor == null)
            return;

        CodeEditor.TextChanged -= OnCodeEditorTextChanged;
        CodeEditor.PointerWheelChanged -= OnPointerWheelChanged;
        CodeEditor.SizeChanged -= OnEditorSizeChanged;
        if (MainGrid != null)
            MainGrid.SizeChanged -= OnEditorSizeChanged;
        if (CodeEditor.TextArea != null)
        {
            CodeEditor.TextArea.Caret.PositionChanged -= OnCaretPositionChanged;
            CodeEditor.TextArea.TextEntered -= OnTextEntered;
            CodeEditor.TextArea.TextEntering -= OnTextEntering;
            CodeEditor.TextArea.RemoveHandler(InputElement.KeyDownEvent, OnEditorKeyDown);
            if (_errorRenderer != null)
                CodeEditor.TextArea.TextView.BackgroundRenderers.Remove(_errorRenderer);
        }

        _signatureHelpHandler?.Detach();
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
        ApplyEditorSettings();
        InitializeErrorRenderer();

        CodeEditor.Document ??= new TextDocument();
        CodeEditor.TextChanged += OnCodeEditorTextChanged;
        CodeEditor.PointerWheelChanged += OnPointerWheelChanged;
        CodeEditor.SizeChanged += OnEditorSizeChanged;
        MainGrid.SizeChanged += OnEditorSizeChanged;
        CodeEditor.TextArea.Caret.PositionChanged += OnCaretPositionChanged;

        CodeEditor.Document.Text = viewModel.CodeText;
        UpdateOutputPanelLayout(viewModel.IsOutputPanelExpanded);
        UpdateCursorPosition();
        ApplyCompilationErrors();
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (viewModel == null) return;

        if (e.PropertyName == nameof(ScriptTabViewModel.IsOutputPanelExpanded))
            UpdateOutputPanelLayout(viewModel.IsOutputPanelExpanded);

        if (e.PropertyName is nameof(ScriptTabViewModel.ShowHtmlOutput)
            or nameof(ScriptTabViewModel.Output)
            or nameof(ScriptTabViewModel.HtmlOutput))
            LoadOutputDocument();

        if (CodeEditor == null) return;

        if (e.PropertyName == nameof(ScriptTabViewModel.CodeText) &&
            CodeEditor.Document.Text != viewModel.CodeText)
        {
            CodeEditor.Document.Text = viewModel.CodeText;
            _signatureHelpHandler?.Reset();
        }

        if (e.PropertyName == nameof(ScriptTabViewModel.CompilationErrors))
            ApplyCompilationErrors();
    }

    private void OnRenameEditStarted() =>
        Dispatcher.UIThread.Post(FocusRenameEditor, DispatcherPriority.Loaded);

    private async void OnRenameTextBoxLostFocus(object? sender, RoutedEventArgs e)
    {
        if (suppressRenameCommit || viewModel is not { IsRenaming: true })
            return;

        suppressRenameCommit = true;
        try
        {
            await viewModel.CommitRenameAsync();
        }
        finally
        {
            suppressRenameCommit = false;
        }
    }

    private async void OnRenameTextBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (viewModel is not { IsRenaming: true })
            return;

        if (e.Key == Key.Enter)
        {
            suppressRenameCommit = true;
            try
            {
                await viewModel.CommitRenameAsync();
            }
            finally
            {
                suppressRenameCommit = false;
            }

            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            suppressRenameCommit = true;
            try
            {
                viewModel.CancelRename();
            }
            finally
            {
                suppressRenameCommit = false;
            }

            e.Handled = true;
        }
    }

    private void OnRenameTextBoxLoaded(object? sender, RoutedEventArgs e) =>
        Dispatcher.UIThread.Post(FocusRenameEditor, DispatcherPriority.Input);

    private void FocusRenameEditor()
    {
        if (RenameTextBox == null || viewModel is not { IsRenaming: true })
            return;

        RenameTextBox.Focus();
        RenameTextBox.SelectAll();
    }

    private void OnViewSizeChanged(object? sender, SizeChangedEventArgs e) =>
        ScheduleEditorViewportFix();

    private void OnEditorSizeChanged(object? sender, SizeChangedEventArgs e) =>
        ScheduleEditorViewportFix();

    private void ScheduleEditorViewportFix()
    {
        if (CodeEditor?.TextArea?.TextView is null)
            return;

        Dispatcher.UIThread.Post(FixEditorViewport, DispatcherPriority.Render);
    }

    private void FixEditorViewport()
    {
        if (CodeEditor?.TextArea?.TextView is not TextView textView)
            return;

        textView.InvalidateMeasure();
        textView.InvalidateArrange();

        var viewportHeight = textView.Bounds.Height;
        if (viewportHeight <= 0 || double.IsNaN(viewportHeight))
            return;

        var maxScroll = Math.Max(0, textView.DocumentHeight - viewportHeight);
        var offsetY = textView.ScrollOffset.Y;
        var clampedY = Math.Clamp(offsetY, 0, maxScroll);
        if (Math.Abs(clampedY - offsetY) > 0.5)
            CodeEditor.ScrollToVerticalOffset(clampedY);
    }

    private void OnCaretPositionChanged(object? sender, EventArgs e) => UpdateCursorPosition();

    private void UpdateCursorPosition()
    {
        if (viewModel == null || CodeEditor?.TextArea == null) return;

        var line = CodeEditor.TextArea.Caret.Line;
        var column = CodeEditor.TextArea.Caret.Column;
        viewModel.CursorPosition = $"{line}:{column}";
    }

    private void UpdateOutputPanelLayout(bool expanded)
    {
        if (MainGrid == null || MainGrid.RowDefinitions.Count < 3) return;

        MainGrid.RowDefinitions[1].Height = expanded ? GridLength.Auto : new GridLength(0);
        MainGrid.RowDefinitions[2].Height = expanded
            ? new GridLength(2, GridUnitType.Star)
            : GridLength.Auto;

        ScheduleEditorViewportFix();
    }

    private void OnCodeEditorTextChanged(object? sender, EventArgs e)
    {
        if (viewModel != null && CodeEditor != null)
            viewModel.CodeText = CodeEditor.Document.Text;

        viewModel?.ClearCompilationErrors();
        _codeCompletionHandler?.OnTextChanged();
    }

    private void InitializeErrorRenderer()
    {
        if (CodeEditor?.TextArea == null) return;

        _errorRenderer = new CompilationErrorRenderer();
        CodeEditor.TextArea.TextView.BackgroundRenderers.Add(_errorRenderer);
    }

    private void ApplyCompilationErrors()
    {
        if (_errorRenderer == null || CodeEditor?.Document == null || viewModel == null)
            return;

        _errorRenderer.SetErrors(
            CodeEditor.Document,
            viewModel.CompilationErrors.Select(e => (e.Line, e.Column, e.EndLine, e.EndColumn)));

        CodeEditor.TextArea.TextView.InvalidateLayer(_errorRenderer.Layer);
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
        CodeEditor.TextArea.AddHandler(InputElement.KeyDownEvent, OnEditorKeyDown, RoutingStrategies.Tunnel);
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

        CodeEditor.TextArea.Caret.CaretBrush = Brush.Parse("#307FFF");
        CodeEditor.Options.HighlightCurrentLine = true;
        CodeEditor.TextArea.SelectionBrush = Brush.Parse("#A6D2FF");
    }

    private void ApplyEditorSettings()
    {
        if (CodeEditor == null) return;

        CodeEditor.FontFamily = new FontFamily(ApplicationSettings.EditorFontFamily);
        CodeEditor.FontSize = ApplicationSettings.EditorFontSize;
        CodeEditor.ShowLineNumbers = ApplicationSettings.ShowLineNumbers;
        CodeEditor.Options.IndentationSize = ApplicationSettings.TabSize;
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

    private void HookOutputWebView()
    {
        if (OutputWebView == null)
            return;

        OutputWebView.EnvironmentRequested += OnOutputEnvironmentRequested;
        OutputWebView.AdapterCreated += OnOutputAdapterCreated;
        OutputWebView.NavigationStarted += OnOutputNavigationStarted;
        OutputWebView.NewWindowRequested += OnOutputNewWindowRequested;
        App.OutputWebViewInitFailed += OnOutputWebViewInitFailed;
        TryShowWebViewUnavailableFallback();
    }

    private void OnOutputWebViewInitFailed(string message) =>
        ShowWebViewFallback($"Output WebView failed to start: {message}");

    private static void OnOutputEnvironmentRequested(object? sender, WebViewEnvironmentRequestedEventArgs e)
    {
        e.EnableDevTools = false;
        switch (e)
        {
            case GtkWebViewEnvironmentRequestedEventArgs gtk:
                gtk.EphemeralDataManager = true;
                gtk.DisableCache = true;
                break;
            case LinuxWpeWebViewEnvironmentRequestedEventArgs wpe:
                wpe.PreferWebKitGtkInstead = false;
                break;
        }
    }

    private void OnOutputAdapterCreated(object? sender, WebViewAdapterEventArgs e)
    {
        HideWebViewFallback();
        LoadOutputDocument();
    }

    private void OnOutputNavigationStarted(object? sender, WebViewNavigationStartingEventArgs e)
    {
        var uri = e.Request;
        if (uri is null)
            return;

        if (uri.Scheme is "about" or "data")
            return;

        e.Cancel = true;
    }

    private void OnOutputNewWindowRequested(object? sender, WebViewNewWindowRequestedEventArgs e) =>
        e.Handled = true;

    private void OnDumpFragmentAppended(string _) => LoadOutputDocument();

    private void OnDumpHtmlCleared() => LoadOutputDocument();

    private void LoadOutputDocument()
    {
        if (OutputWebView == null || viewModel == null)
            return;

        var html = viewModel.ShowHtmlOutput
            ? viewModel.HtmlOutput
            : HtmlDumpService.BuildTextDocument(viewModel.OutputDisplayHtml);

        try
        {
            OutputWebView.NavigateToString(html);
        }
        catch (Exception ex)
        {
            ShowWebViewFallback($"Output WebView failed to start: {ex.Message}");
        }
    }

    private void ShowWebViewFallback(string message)
    {
        if (OutputWebViewFallback == null)
            return;

        OutputWebViewFallback.Text = message;
        OutputWebViewFallback.IsVisible = true;
        if (OutputWebView != null)
            OutputWebView.IsVisible = false;
    }

    private void HideWebViewFallback()
    {
        if (OutputWebViewFallback != null)
            OutputWebViewFallback.IsVisible = false;
        if (OutputWebView != null)
            OutputWebView.IsVisible = true;
    }

    private void TryShowWebViewUnavailableFallback()
    {
        if (!OperatingSystem.IsLinux())
            return;

        var gtk = WebViewAdapterInfo.GetAdapterInfo(WebViewAdapterType.WebKitGtk);
        var wpe = WebViewAdapterInfo.GetAdapterInfo(WebViewAdapterType.WpeWebKit);
        if (gtk.IsInstalled || wpe.IsInstalled)
            return;

        var reason = gtk.UnavailableReason ?? wpe.UnavailableReason
            ?? "WebKitGTK is not installed.";
        ShowWebViewFallback(
            $"{reason} Install: sudo apt install libgtk-3-0 libwebkit2gtk-4.1-0 libsoup-3.0-0");
    }
}
