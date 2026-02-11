using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using AvaloniaEdit.CodeCompletion;
using ScratchpadSharp.ViewModels;
using ScratchpadSharp.Views;

namespace ScratchpadSharp.Editor;

public class SignatureHelpWindow : CompletionWindowBase
{
    public SignatureHelpViewModel ViewModel { get; }

    public SignatureHelpWindow(AvaloniaEdit.Editing.TextArea textArea) : base(textArea)
    {
        ViewModel = new SignatureHelpViewModel();
        var popup = new SignatureHelpPopup { DataContext = ViewModel };

        var border = new Border
        {
            Child = popup,
            MinWidth = 450,
            MaxWidth = 600
        };

        Child = border;
    }

    protected override void OnClosed()
    {
        base.OnClosed();
        ViewModel.Hide();
    }
}
