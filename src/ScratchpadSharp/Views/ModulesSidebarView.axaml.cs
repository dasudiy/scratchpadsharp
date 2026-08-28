using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using ScratchpadSharp.ViewModels;

namespace ScratchpadSharp.Views;

public partial class ModulesSidebarView : UserControl
{
    public ModulesSidebarView()
    {
        InitializeComponent();

        ModuleTree.AddHandler(
            InputElement.PointerReleasedEvent,
            OnTreePointerReleased,
            RoutingStrategies.Bubble,
            handledEventsToo: true);
    }

    private void OnTreePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        var treeViewItem = (e.Source as Control)?.FindAncestorOfType<TreeViewItem>();
        if (treeViewItem?.DataContext is ModuleTreeNode node && DataContext is ModulesSidebarViewModel vm)
            vm.SelectedNode = node;

        if (e.InitialPressMouseButton == MouseButton.Right)
            return;

        if (treeViewItem is null || treeViewItem.ItemCount == 0)
            return;

        treeViewItem.IsExpanded = !treeViewItem.IsExpanded;
        e.Handled = true;
    }
}
