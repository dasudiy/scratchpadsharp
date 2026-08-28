using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using ScratchpadSharp.ViewModels;

namespace ScratchpadSharp.Views;

public partial class QueriesSidebarView : UserControl
{
    public QueriesSidebarView()
    {
        InitializeComponent();

        QueryTree.AddHandler(
            InputElement.PointerReleasedEvent,
            OnTreePointerReleased,
            RoutingStrategies.Bubble,
            handledEventsToo: true);

        QueryTree.DoubleTapped += OnTreeDoubleTapped;
    }

    private void OnTreePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        var treeViewItem = (e.Source as Control)?.FindAncestorOfType<TreeViewItem>();
        if (treeViewItem?.DataContext is QueryTreeNode node && DataContext is QueriesSidebarViewModel vm)
            vm.SelectedNode = node;

        if (e.InitialPressMouseButton == MouseButton.Right)
            return;

        if (treeViewItem is null || treeViewItem.ItemCount == 0)
            return;

        treeViewItem.IsExpanded = !treeViewItem.IsExpanded;
        e.Handled = true;
    }

    private void OnRootRowPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton == MouseButton.Right)
            return;

        if ((e.Source as Control)?.FindAncestorOfType<Button>() != null)
            return;

        if (DataContext is QueriesSidebarViewModel { RootNode: { } root })
            root.IsExpanded = !root.IsExpanded;

        e.Handled = true;
    }

    private async void OnTreeDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is not QueriesSidebarViewModel vm)
            return;

        var treeViewItem = (e.Source as Control)?.FindAncestorOfType<TreeViewItem>();
        if (treeViewItem?.DataContext is QueryTreeNode node)
            vm.SelectedNode = node;

        await vm.OpenSelectedAsync();
    }
}
