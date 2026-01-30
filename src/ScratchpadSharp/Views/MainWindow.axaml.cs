using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using AvaloniaEdit;
using AvaloniaEdit.Document;
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

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
