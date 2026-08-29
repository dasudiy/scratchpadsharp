using System;
using System.Linq;
using System.Reactive;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ScratchpadSharp.ViewModels;

namespace ScratchpadSharp.Views;

public partial class QueriesSidebarView : UserControl
{
    private static readonly DataFormat<string> QueryPathDragFormat =
        DataFormat.CreateStringApplicationFormat("ScratchpadSharp.QueryPath");

    private const double DragThreshold = 6;

    private QueriesSidebarViewModel? viewModel;
    private bool suppressRenameCommit;
    private QueryTreeNode? pendingDragNode;
    private Point? pendingDragStart;
    private bool dragInProgress;
    private Control? dropHighlightTarget;

    public QueriesSidebarView()
    {
        InitializeComponent();

        DataContextChanged += OnDataContextChanged;

        QueryTree.AddHandler(
            InputElement.PointerPressedEvent,
            OnTreePointerPressed,
            RoutingStrategies.Tunnel);

        QueryTree.AddHandler(
            InputElement.PointerMovedEvent,
            OnTreePointerMoved,
            RoutingStrategies.Tunnel);

        QueryTree.AddHandler(
            InputElement.PointerReleasedEvent,
            OnTreePointerReleased,
            RoutingStrategies.Bubble,
            handledEventsToo: true);

        QueryTree.AddHandler(
            InputElement.KeyDownEvent,
            OnTreeKeyDown,
            RoutingStrategies.Tunnel);

        QueryTree.DoubleTapped += OnTreeDoubleTapped;

        DragDrop.SetAllowDrop(QueryTree, true);
        DragDrop.SetAllowDrop(QueryRootRow, true);

        QueryTree.AddHandler(DragDrop.DragOverEvent, OnDragOver, RoutingStrategies.Bubble);
        QueryTree.AddHandler(DragDrop.DragLeaveEvent, OnDragLeave, RoutingStrategies.Bubble);
        QueryTree.AddHandler(DragDrop.DropEvent, OnDrop, RoutingStrategies.Bubble);

        QueryRootRow.AddHandler(DragDrop.DragOverEvent, OnDragOver, RoutingStrategies.Bubble);
        QueryRootRow.AddHandler(DragDrop.DragLeaveEvent, OnDragLeave, RoutingStrategies.Bubble);
        QueryRootRow.AddHandler(DragDrop.DropEvent, OnDrop, RoutingStrategies.Bubble);

        if (QueryTree.ContextMenu is ContextMenu treeMenu)
        {
            treeMenu.Opening += OnTreeContextMenuOpening;
            treeMenu.Closed += OnTreeContextMenuClosed;
        }
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (viewModel != null)
            viewModel.RenameEditStarted -= OnRenameEditStarted;

        viewModel = DataContext as QueriesSidebarViewModel;
        if (viewModel != null)
            viewModel.RenameEditStarted += OnRenameEditStarted;
    }

    private void OnRenameEditStarted() =>
        Dispatcher.UIThread.Post(FocusRenameEditor, DispatcherPriority.Loaded);

    private void OnTreeContextMenuClosed(object? sender, RoutedEventArgs e)
    {
        if (viewModel?.EditingNode is { IsEditing: true })
            Dispatcher.UIThread.Post(FocusRenameEditor, DispatcherPriority.Input);
    }

    private void OnTreeContextMenuOpening(object? sender, EventArgs e)
    {
        if (viewModel == null || sender is not ContextMenu menu)
            return;

        menu.DataContext = viewModel;

        if (menu.PlacementTarget is Control target)
        {
            var treeViewItem = target.FindAncestorOfType<TreeViewItem>();
            if (treeViewItem?.DataContext is QueryTreeNode node)
                viewModel.SelectedNode = node;
        }
    }

    private void OnTreePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var properties = e.GetCurrentPoint(QueryTree).Properties;
        if (!properties.IsLeftButtonPressed)
            return;

        if ((e.Source as Control)?.FindAncestorOfType<TextBox>() != null)
            return;

        var node = GetNodeFromSource(e.Source as Control);
        if (!CanDragNode(node))
        {
            pendingDragNode = null;
            pendingDragStart = null;
            return;
        }

        pendingDragNode = node;
        pendingDragStart = e.GetPosition(QueryTree);
    }

    private async void OnTreePointerMoved(object? sender, PointerEventArgs e)
    {
        if (pendingDragNode == null || pendingDragStart == null || dragInProgress)
            return;

        if (!e.GetCurrentPoint(QueryTree).Properties.IsLeftButtonPressed)
            return;

        var position = e.GetPosition(QueryTree);
        var delta = position - pendingDragStart.Value;
        if (Math.Abs(delta.X) < DragThreshold && Math.Abs(delta.Y) < DragThreshold)
            return;

        var sourcePath = pendingDragNode.FullPath;
        pendingDragNode = null;
        pendingDragStart = null;
        dragInProgress = true;

        try
        {
            var dragData = new DataTransfer();
            dragData.Add(DataTransferItem.Create(QueryPathDragFormat, sourcePath));
            await DragDrop.DoDragDropAsync(e, dragData, DragDropEffects.Move);
        }
        finally
        {
            dragInProgress = false;
            ClearDropHighlight();
        }
    }

    private async void OnTreePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        pendingDragNode = null;
        pendingDragStart = null;

        if (dragInProgress)
        {
            e.Handled = true;
            return;
        }

        var treeViewItem = (e.Source as Control)?.FindAncestorOfType<TreeViewItem>();
        var clickedNode = treeViewItem?.DataContext as QueryTreeNode;

        if (viewModel != null && viewModel.EditingNode is { IsEditing: true } editing
            && clickedNode != null && !ReferenceEquals(editing, clickedNode))
        {
            await viewModel.CommitRenameAsync(editing);
        }

        if (clickedNode != null && viewModel != null)
            viewModel.SelectedNode = clickedNode;

        if (e.InitialPressMouseButton == MouseButton.Right)
            return;

        if ((e.Source as Control)?.FindAncestorOfType<TextBox>() != null)
            return;

        if (clickedNode is { IsEditing: true })
            return;

        if (clickedNode is not { Kind: QueryNodeKind.Directory } || treeViewItem is null)
            return;

        QueryTree.Focus();
        treeViewItem.IsExpanded = !treeViewItem.IsExpanded;
        e.Handled = true;
    }

    private void OnRootRowPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (viewModel?.RootNode is { } root)
            viewModel.SelectedNode = root;

        if (e.InitialPressMouseButton == MouseButton.Right)
            return;

        if ((e.Source as Control)?.FindAncestorOfType<Button>() != null)
            return;

        if (viewModel?.RootNode is { } rootNode)
            rootNode.IsExpanded = !rootNode.IsExpanded;

        e.Handled = true;
    }

    private async void OnTreeDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (viewModel == null)
            return;

        var treeViewItem = (e.Source as Control)?.FindAncestorOfType<TreeViewItem>();
        if (treeViewItem?.DataContext is QueryTreeNode node)
            viewModel.SelectedNode = node;

        await viewModel.OpenSelectedAsync();
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (!TryGetDraggedPath(e, out var sourcePath))
        {
            e.DragEffects = DragDropEffects.None;
            ClearDropHighlight();
            return;
        }

        var targetNode = GetDropTarget(e);
        if (targetNode == null || !QueryPathOperations.CanMoveTo(sourcePath, targetNode.FullPath))
        {
            e.DragEffects = DragDropEffects.None;
            ClearDropHighlight();
            return;
        }

        SetDropHighlight(GetDropHighlightTarget(e, targetNode));
        e.DragEffects = DragDropEffects.Move;
        e.Handled = true;
    }

    private void OnDragLeave(object? sender, DragEventArgs e)
    {
        ClearDropHighlight();
        e.Handled = true;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        ClearDropHighlight();

        if (viewModel == null || !TryGetDraggedPath(e, out var sourcePath))
            return;

        var targetNode = GetDropTarget(e);
        if (targetNode == null)
            return;

        e.Handled = true;
        e.DragEffects = DragDropEffects.Move;

        await viewModel.MoveItemByPathAsync(sourcePath, targetNode);
    }

    private bool TryGetDraggedPath(DragEventArgs e, out string sourcePath)
    {
        sourcePath = string.Empty;
        if (!e.DataTransfer.Contains(QueryPathDragFormat))
            return false;

        var path = e.DataTransfer.TryGetValue(QueryPathDragFormat);
        if (string.IsNullOrWhiteSpace(path))
            return false;

        sourcePath = path;
        return true;
    }

    private QueryTreeNode? GetDropTarget(DragEventArgs e)
    {
        var point = e.GetPosition(QueryTree);
        var hit = QueryTree.InputHitTest(point) as Control;
        var treeViewItem = hit?.FindAncestorOfType<TreeViewItem>();
        if (treeViewItem?.DataContext is QueryTreeNode node && CanDropOnNode(node))
            return node;

        var rootPoint = e.GetPosition(QueryRootRow);
        if (QueryRootRow.InputHitTest(rootPoint) != null && viewModel?.RootNode is { } root)
            return root;

        return null;
    }

    private Control? GetDropHighlightTarget(DragEventArgs e, QueryTreeNode targetNode)
    {
        if (targetNode.IsRoot)
            return QueryRootRow;

        var point = e.GetPosition(QueryTree);
        var hit = QueryTree.InputHitTest(point) as Control;
        return hit?.FindAncestorOfType<TreeViewItem>();
    }

    private void SetDropHighlight(Control? target)
    {
        if (ReferenceEquals(dropHighlightTarget, target))
            return;

        ClearDropHighlight();
        dropHighlightTarget = target;
        dropHighlightTarget?.Classes.Add("drag-over");
    }

    private void ClearDropHighlight()
    {
        dropHighlightTarget?.Classes.Remove("drag-over");
        dropHighlightTarget = null;
    }

    private static bool CanDragNode(QueryTreeNode? node) =>
        node is { IsPlaceholder: false, IsRoot: false };

    private static bool CanDropOnNode(QueryTreeNode node) =>
        !node.IsPlaceholder && (node.IsRoot || node.Kind == QueryNodeKind.Directory);

    private static QueryTreeNode? GetNodeFromSource(Control? source)
    {
        var treeViewItem = source?.FindAncestorOfType<TreeViewItem>();
        return treeViewItem?.DataContext as QueryTreeNode;
    }

    private async void OnRenameTextBoxLostFocus(object? sender, RoutedEventArgs e)
    {
        if (suppressRenameCommit
            || sender is not TextBox { DataContext: QueryTreeNode node }
            || viewModel == null
            || !node.IsEditing)
            return;

        suppressRenameCommit = true;
        try
        {
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                await Task.Yield();
                if (!node.IsEditing)
                    return;

                var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
                if (focused is TextBox textBox && ReferenceEquals(textBox.DataContext, node))
                    return;

                await viewModel.CommitRenameAsync(node);
            });
        }
        finally
        {
            suppressRenameCommit = false;
        }
    }

    private async void OnRenameTextBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox { DataContext: QueryTreeNode node } || viewModel == null)
            return;

        if (e.Key == Key.Enter)
        {
            suppressRenameCommit = true;
            try
            {
                await viewModel.CommitRenameAsync(node);
            }
            finally
            {
                suppressRenameCommit = false;
            }

            QueryTree.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            suppressRenameCommit = true;
            try
            {
                viewModel.CancelRenameEdit(node);
            }
            finally
            {
                suppressRenameCommit = false;
            }

            QueryTree.Focus();
            e.Handled = true;
        }
    }

    private void OnRenameTextBoxLoaded(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox { DataContext: QueryTreeNode { IsEditing: true } })
            Dispatcher.UIThread.Post(FocusRenameEditor, DispatcherPriority.Input);
    }

    private void OnTreeKeyDown(object? sender, KeyEventArgs e) => HandleShortcutKey(e);

    private void OnKeyDown(object? sender, KeyEventArgs e) => HandleShortcutKey(e);

    private void HandleShortcutKey(KeyEventArgs e)
    {
        if (viewModel == null)
            return;

        if (e.Key == Key.F2 && viewModel.CanRenameSelected)
        {
            viewModel.RenameCommand.Execute(Unit.Default);
            e.Handled = true;
        }
        else if (e.Key == Key.Delete && viewModel.CanDeleteSelected)
        {
            viewModel.DeleteCommand.Execute(Unit.Default);
            e.Handled = true;
        }
    }

    private void FocusRenameEditor()
    {
        if (viewModel?.EditingNode is not { IsEditing: true } node)
            return;

        var textBox = FindTreeViewItem(QueryTree, node)?
            .GetVisualDescendants()
            .OfType<TextBox>()
            .FirstOrDefault();

        if (textBox == null)
            return;

        suppressRenameCommit = true;
        try
        {
            textBox.Focus();
            textBox.SelectAll();
        }
        finally
        {
            suppressRenameCommit = false;
        }
    }

    private static TreeViewItem? FindTreeViewItem(Control parent, object dataContext)
    {
        foreach (var descendant in parent.GetVisualDescendants())
        {
            if (descendant is TreeViewItem { DataContext: var dc } item && ReferenceEquals(dc, dataContext))
                return item;
        }

        return null;
    }
}
