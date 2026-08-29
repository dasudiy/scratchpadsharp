using System;
using System.Reactive;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using ScratchpadSharp.ViewModels;

namespace ScratchpadSharp.Views;

public partial class ModulesSidebarView : UserControl
{
    private ModulesSidebarViewModel? viewModel;

    public ModulesSidebarView()
    {
        InitializeComponent();

        DataContextChanged += OnDataContextChanged;

        ModuleTree.AddHandler(
            InputElement.PointerReleasedEvent,
            OnTreePointerReleased,
            RoutingStrategies.Bubble,
            handledEventsToo: true);

        ModuleTree.DoubleTapped += OnTreeDoubleTapped;

        if (ModuleTree.ContextMenu is ContextMenu treeMenu)
            treeMenu.Opening += OnContextMenuOpening;

        if (ModuleRootRow.ContextMenu is ContextMenu rootMenu)
            rootMenu.Opening += OnRootContextMenuOpening;
    }

    private void OnDataContextChanged(object? sender, EventArgs e) =>
        viewModel = DataContext as ModulesSidebarViewModel;

    private void OnTreePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        var treeViewItem = (e.Source as Control)?.FindAncestorOfType<TreeViewItem>();
        if (treeViewItem?.DataContext is ModuleTreeNode node && viewModel != null)
            viewModel.SelectedNode = node;

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

        if (DataContext is ModulesSidebarViewModel { RootNode: { } root })
            root.IsExpanded = !root.IsExpanded;

        e.Handled = true;
    }

    private void OnTreeDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (viewModel == null)
            return;

        var treeViewItem = (e.Source as Control)?.FindAncestorOfType<TreeViewItem>();
        if (treeViewItem?.DataContext is ModuleTreeNode node)
            viewModel.SelectedNode = node;

        if (viewModel.IsTableOrViewSelected)
            viewModel.Take100Command.Execute(Unit.Default);
    }

    private void OnContextMenuOpening(object? sender, EventArgs e)
    {
        if (viewModel == null || sender is not ContextMenu menu)
            return;

        menu.DataContext = viewModel;

        if (menu.PlacementTarget is Control target)
        {
            var treeViewItem = target.FindAncestorOfType<TreeViewItem>();
            if (treeViewItem?.DataContext is ModuleTreeNode node)
                viewModel.SelectedNode = node;
        }
    }

    private void OnRootContextMenuOpening(object? sender, EventArgs e)
    {
        if (viewModel == null || sender is not ContextMenu menu)
            return;

        menu.DataContext = viewModel;
        viewModel.SelectedNode = viewModel.RootNode;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (viewModel == null)
            return;

        if (e.Key == Key.Delete && viewModel.IsInstanceSelected)
        {
            viewModel.DeleteInstanceCommand.Execute(Unit.Default);
            e.Handled = true;
        }
    }
}
