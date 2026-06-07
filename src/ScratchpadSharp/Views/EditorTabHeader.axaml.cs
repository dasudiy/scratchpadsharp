using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace ScratchpadSharp.Views;

public partial class EditorTabHeader : UserControl
{
    public static readonly StyledProperty<bool> IsCloseVisibleProperty =
        AvaloniaProperty.Register<EditorTabHeader, bool>(nameof(IsCloseVisible));

    public bool IsCloseVisible
    {
        get => GetValue(IsCloseVisibleProperty);
        set => SetValue(IsCloseVisibleProperty, value);
    }

    public EditorTabHeader()
    {
        InitializeComponent();
    }

    private void OnPointerEntered(object? sender, PointerEventArgs e) => IsCloseVisible = true;

    private void OnPointerExited(object? sender, PointerEventArgs e) => IsCloseVisible = false;
}
