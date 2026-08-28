using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace ScratchpadSharp.Views.Dock;

public partial class QueriesToolView : UserControl
{
    public QueriesToolView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
