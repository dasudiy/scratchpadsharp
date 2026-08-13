using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using ScratchpadSharp.ViewModels;

namespace ScratchpadSharp.Views;

public partial class ModulesSidebarView : UserControl
{
    public ModulesSidebarView()
    {
        InitializeComponent();
    }

    private void OnTreePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
            return;
        if (DataContext is not ModulesSidebarViewModel vm)
            return;

        Control? current = e.Source as Control;
        while (current != null && current != sender)
        {
            if (current.DataContext is ModuleTreeNode node)
            {
                vm.SelectedNode = node;
                return;
            }

            current = current.GetVisualParent<Control>();
        }
    }
}
