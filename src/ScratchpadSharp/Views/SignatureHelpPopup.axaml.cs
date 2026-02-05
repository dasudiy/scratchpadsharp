using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using ScratchpadSharp.ViewModels;

namespace ScratchpadSharp.Views;

public partial class SignatureHelpPopup : UserControl
{
    public SignatureHelpPopup()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
