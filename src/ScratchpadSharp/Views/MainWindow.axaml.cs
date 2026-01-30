using System;
using System.Diagnostics;
using System.Xml;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Highlighting.Xshd;
using ScratchpadSharp.ViewModels;

namespace ScratchpadSharp.Views;

public partial class MainWindow : Window
{
    private TextEditor? codeEditor;
    private MainWindowViewModel? viewModel;

    public MainWindow()
    {
        InitializeComponent();
        
        codeEditor = this.FindControl<TextEditor>("CodeEditor");
        
        if (codeEditor != null)
        {
            InitializeSyntaxHighlighting();
            
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
