using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using ReactiveUI;
using ScratchpadSharp.Core.Configuration;
using ScratchpadSharp.Core.Storage;
using ScratchpadSharp.Views;

namespace ScratchpadSharp.ViewModels;

public enum QueryNodeKind
{
    Directory,
    PackageFile,
    FolderPackage
}

public class QueryTreeNode : ReactiveObject
{
    private bool isExpanded;
    private bool isLoaded;

    public string Name { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public QueryNodeKind Kind { get; set; }
    public bool IsPlaceholder { get; set; }
    public bool IsRoot { get; set; }

    private bool isEditing;
    private string editName = string.Empty;

    public bool IsEditing
    {
        get => isEditing;
        set => this.RaiseAndSetIfChanged(ref isEditing, value);
    }

    public string EditName
    {
        get => editName;
        set => this.RaiseAndSetIfChanged(ref editName, value);
    }

    public bool IsExpanded
    {
        get => isExpanded;
        set
        {
            if (!this.RaiseAndSetIfChanged(ref isExpanded, value))
                return;

            if (value && !IsLoaded && Kind == QueryNodeKind.Directory && LoadChildrenAsync is not null)
                _ = LoadChildrenAsync();
        }
    }

    public bool IsLoaded
    {
        get => isLoaded;
        set => this.RaiseAndSetIfChanged(ref isLoaded, value);
    }

    public ObservableCollection<QueryTreeNode> Children { get; } = new();

    public Func<Task>? LoadChildrenAsync { get; set; }
}

public class QueriesSidebarViewModel : ReactiveObject
{
    private readonly Func<string, Task> openQueryAsync;
    private Func<Window?> getOwnerWindow = () => null;
    private Func<Task<string?>> showSavePackageDialogAsync = () => Task.FromResult<string?>(null);
    private Func<string, Task> closeQueryTabsByPathAsync = _ => Task.CompletedTask;
    private Func<string, string, Task> renameQueryTabPathAsync = (_, _) => Task.CompletedTask;
    private Func<string, Task> saveQueryByPathAsync = _ => Task.CompletedTask;
    private Func<string, string, Task> reopenQueryTabsAtPathAsync = (_, _) => Task.CompletedTask;
    private Func<string, Task> createNewQueryInFolderAsync = _ => Task.CompletedTask;

    private QueryTreeNode? rootNode;
    private string statusText = string.Empty;
    private QueryTreeNode? selectedNode;
    private QueryTreeNode? editingNode;

    public QueriesSidebarViewModel(Func<string, Task> openQueryAsync)
    {
        this.openQueryAsync = openQueryAsync;

        var selected = this.WhenAnyValue(x => x.SelectedNode);

        RefreshCommand = ReactiveCommand.CreateFromTask(RefreshAsync);
        OpenSelectedCommand = ReactiveCommand.CreateFromTask(OpenSelectedAsync,
            selected.Select(node => node is { IsPlaceholder: false }
                && node.Kind is QueryNodeKind.PackageFile or QueryNodeKind.FolderPackage));
        NewFolderCommand = ReactiveCommand.CreateFromTask(NewFolderAsync,
            selected.Select(CanNewFolder));
        NewQueryCommand = ReactiveCommand.CreateFromTask(NewQueryAsync,
            selected.Select(CanNewFolder));
        RenameCommand = ReactiveCommand.Create(BeginRenameEdit,
            selected.Select(CanRename));
        DeleteCommand = ReactiveCommand.CreateFromTask(DeleteAsync,
            selected.Select(CanDelete));
        OpenContainingFolderCommand = ReactiveCommand.CreateFromTask(OpenContainingFolderAsync,
            selected.Select(CanOpenContainingFolder));
        PackCommand = ReactiveCommand.CreateFromTask(PackAsync,
            selected.Select(node => node?.Kind == QueryNodeKind.FolderPackage));
        UnpackCommand = ReactiveCommand.CreateFromTask(UnpackAsync,
            selected.Select(node => node?.Kind == QueryNodeKind.PackageFile));

        ApplicationSettings.Changed += OnSettingsChanged;
        _ = RefreshAsync();
    }

    public void ConfigureDialogs(
        Func<Window?> ownerWindowProvider,
        Func<Task<string?>> savePackageDialog,
        Func<string, Task> closeQueryTabsByPath,
        Func<string, string, Task> renameQueryTabPath,
        Func<string, Task> saveQueryByPath,
        Func<string, string, Task> reopenQueryTabsAtPath,
        Func<string, Task> createNewQueryInFolder)
    {
        getOwnerWindow = ownerWindowProvider;
        showSavePackageDialogAsync = savePackageDialog;
        closeQueryTabsByPathAsync = closeQueryTabsByPath;
        renameQueryTabPathAsync = renameQueryTabPath;
        saveQueryByPathAsync = saveQueryByPath;
        reopenQueryTabsAtPathAsync = reopenQueryTabsAtPath;
        createNewQueryInFolderAsync = createNewQueryInFolder;
    }

    public async Task<bool> MoveItemByPathAsync(string sourcePath, QueryTreeNode targetDirectory)
    {
        if (targetDirectory is { IsPlaceholder: true }
            || (!targetDirectory.IsRoot && targetDirectory.Kind != QueryNodeKind.Directory))
            return false;

        if (!QueryPathOperations.CanMoveTo(sourcePath, targetDirectory.FullPath))
            return false;

        var itemName = Path.GetFileName(sourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var targetName = targetDirectory.IsRoot
            ? targetDirectory.Name
            : Path.GetFileName(targetDirectory.FullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        var owner = getOwnerWindow();
        if (!await ConfirmWindow.ConfirmAsync(owner, "Move", $"Move '{itemName}' to '{targetName}'?"))
            return false;

        var sourceParentPath = Path.GetDirectoryName(Path.GetFullPath(sourcePath))!;
        var targetDirectoryPath = targetDirectory.FullPath;

        var newPath = QueryPathOperations.TryMove(sourcePath, targetDirectoryPath, out var error);
        if (newPath == null)
        {
            StatusText = error ?? "Move failed";
            return false;
        }

        try
        {
            await reopenQueryTabsAtPathAsync(sourcePath, newPath);
            await ReloadDirectoryAtPathAsync(sourceParentPath);
            if (!string.Equals(sourceParentPath, targetDirectoryPath, StringComparison.OrdinalIgnoreCase))
                await ReloadDirectoryAtPathAsync(targetDirectoryPath);

            StatusText = $"Moved to {targetName}";
            return true;
        }
        catch (Exception ex)
        {
            StatusText = $"Move failed: {ex.Message}";
            return false;
        }
    }

    public event Action? RenameEditStarted;

    public QueryTreeNode? EditingNode => editingNode;

    public QueryTreeNode? RootNode
    {
        get => rootNode;
        private set => this.RaiseAndSetIfChanged(ref rootNode, value);
    }

    public QueryTreeNode? SelectedNode
    {
        get => selectedNode;
        set
        {
            this.RaiseAndSetIfChanged(ref selectedNode, value);
            this.RaisePropertyChanged(nameof(IsDirectorySelected));
            this.RaisePropertyChanged(nameof(IsPackageSelected));
            this.RaisePropertyChanged(nameof(IsFolderPackageSelected));
            this.RaisePropertyChanged(nameof(IsPackageFileSelected));
            this.RaisePropertyChanged(nameof(CanRenameSelected));
            this.RaisePropertyChanged(nameof(CanDeleteSelected));
        }
    }

    public bool IsDirectorySelected =>
        SelectedNode is { Kind: QueryNodeKind.Directory };

    public bool IsPackageSelected =>
        SelectedNode is { Kind: QueryNodeKind.PackageFile or QueryNodeKind.FolderPackage };

    public bool IsFolderPackageSelected =>
        SelectedNode?.Kind == QueryNodeKind.FolderPackage;

    public bool IsPackageFileSelected =>
        SelectedNode?.Kind == QueryNodeKind.PackageFile;

    public bool CanRenameSelected => CanRename(SelectedNode);

    public bool CanDeleteSelected => CanDelete(SelectedNode);

    public string StatusText
    {
        get => statusText;
        set => this.RaiseAndSetIfChanged(ref statusText, value);
    }

    public ReactiveCommand<Unit, Unit> RefreshCommand { get; }
    public ReactiveCommand<Unit, Unit> OpenSelectedCommand { get; }
    public ReactiveCommand<Unit, Unit> NewFolderCommand { get; }
    public ReactiveCommand<Unit, Unit> NewQueryCommand { get; }
    public ReactiveCommand<Unit, Unit> RenameCommand { get; }
    public ReactiveCommand<Unit, Unit> DeleteCommand { get; }
    public ReactiveCommand<Unit, Unit> OpenContainingFolderCommand { get; }
    public ReactiveCommand<Unit, Unit> PackCommand { get; }
    public ReactiveCommand<Unit, Unit> UnpackCommand { get; }

    public void RequestRefresh() => _ = RefreshAsync();

    public Task OpenSelectedAsync()
    {
        if (SelectedNode is not { IsPlaceholder: false } node
            || node.Kind is QueryNodeKind.Directory)
            return Task.CompletedTask;

        return openQueryAsync(node.FullPath);
    }

    private static bool CanNewFolder(QueryTreeNode? node) =>
        node is { IsPlaceholder: false, Kind: QueryNodeKind.Directory };

    private static bool CanRename(QueryTreeNode? node) =>
        node is { IsPlaceholder: false, IsRoot: false };

    private static bool CanDelete(QueryTreeNode? node) =>
        node is { IsPlaceholder: false, IsRoot: false };

    private static bool CanOpenContainingFolder(QueryTreeNode? node) =>
        node is { IsPlaceholder: false, IsRoot: false };

    private void OnSettingsChanged() => _ = RefreshAsync();

    private async Task RefreshAsync()
    {
        try
        {
            if (editingNode != null)
                CancelRenameEdit(editingNode);

            var rootPath = ApplicationSettings.GetEffectiveQueryDirectory();
            Directory.CreateDirectory(rootPath);

            var root = CreateDirectoryNode(rootPath, isRoot: true);
            await root.LoadChildrenAsync!.Invoke();
            root.IsExpanded = true;
            RootNode = root;

            StatusText = rootPath;
        }
        catch (Exception ex)
        {
            StatusText = $"Refresh failed: {ex.Message}";
        }
    }

    private async Task NewFolderAsync()
    {
        var parentPath = SelectedNode is { Kind: QueryNodeKind.Directory } directory
            ? directory.FullPath
            : RootNode?.FullPath;

        if (string.IsNullOrEmpty(parentPath))
            return;

        var owner = getOwnerWindow();
        var name = await ConfirmWindow.PromptAsync(owner, "New Folder", "Folder name:", "New Folder");
        if (string.IsNullOrWhiteSpace(name))
            return;

        name = QueryPathOperations.SanitizeName(name);
        var folderPath = Path.Combine(parentPath, name);
        if (Directory.Exists(folderPath) || File.Exists(folderPath))
        {
            StatusText = $"Already exists: {name}";
            return;
        }

        Directory.CreateDirectory(folderPath);
        await ReloadDirectoryNodeAsync(SelectedNode is { Kind: QueryNodeKind.Directory } ? SelectedNode : RootNode);
        StatusText = $"Created folder: {name}";
    }

    private async Task NewQueryAsync()
    {
        var parentPath = SelectedNode is { Kind: QueryNodeKind.Directory } directory
            ? directory.FullPath
            : RootNode?.FullPath;

        if (string.IsNullOrEmpty(parentPath))
            return;

        try
        {
            await createNewQueryInFolderAsync(parentPath);
            await ReloadDirectoryNodeAsync(SelectedNode is { Kind: QueryNodeKind.Directory } ? SelectedNode : RootNode);
        }
        catch (Exception ex)
        {
            StatusText = $"New query failed: {ex.Message}";
        }
    }

    private void BeginRenameEdit()
    {
        if (SelectedNode is not { IsPlaceholder: false, IsRoot: false } node)
            return;

        if (editingNode != null && editingNode != node)
            CancelRenameEdit(editingNode);

        editingNode = node;
        node.EditName = node.Name;
        node.IsEditing = true;
        RenameEditStarted?.Invoke();
    }

    public void CancelRenameEdit(QueryTreeNode node)
    {
        node.IsEditing = false;
        node.EditName = node.Name;
        if (editingNode == node)
            editingNode = null;
    }

    public async Task CommitRenameAsync(QueryTreeNode node)
    {
        if (!node.IsEditing)
            return;

        node.IsEditing = false;
        if (editingNode == node)
            editingNode = null;

        var newName = QueryPathOperations.SanitizeName(node.EditName.Trim());
        if (string.IsNullOrWhiteSpace(newName) || string.Equals(newName, node.Name, StringComparison.Ordinal))
        {
            node.EditName = node.Name;
            return;
        }

        var oldPath = node.FullPath;
        var newPath = QueryPathOperations.TryRename(oldPath, newName, out var error);
        if (newPath == null)
        {
            node.EditName = node.Name;
            StatusText = error ?? "Rename failed";
            return;
        }

        try
        {
            await renameQueryTabPathAsync(oldPath, newPath);
            await ReloadDirectoryParentAsync(node);
            StatusText = $"Renamed to {newName}";
        }
        catch (Exception ex)
        {
            node.EditName = node.Name;
            StatusText = $"Rename failed: {ex.Message}";
        }
    }

    private async Task DeleteAsync()
    {
        if (SelectedNode is not { IsPlaceholder: false, IsRoot: false } node)
            return;

        var owner = getOwnerWindow();
        if (!await ConfirmWindow.ConfirmAsync(owner, "Delete", $"Delete '{node.Name}'?"))
            return;

        try
        {
            await closeQueryTabsByPathAsync(node.FullPath);

            if (node.Kind == QueryNodeKind.PackageFile)
                File.Delete(node.FullPath);
            else
                Directory.Delete(node.FullPath, recursive: true);

            await ReloadDirectoryParentAsync(node);
            SelectedNode = null;
            StatusText = $"Deleted {node.Name}";
        }
        catch (Exception ex)
        {
            StatusText = $"Delete failed: {ex.Message}";
        }
    }

    private Task OpenContainingFolderAsync()
    {
        if (SelectedNode is not { IsPlaceholder: false, IsRoot: false } node)
            return Task.CompletedTask;

        try
        {
            var path = node.FullPath;
            if (node.Kind == QueryNodeKind.PackageFile)
            {
                var parent = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(parent))
                    OpenInFileManager(parent, path);
            }
            else
            {
                OpenInFileManager(path, null);
            }

            StatusText = "Opened containing folder";
        }
        catch (Exception ex)
        {
            StatusText = $"Open folder failed: {ex.Message}";
        }

        return Task.CompletedTask;
    }

    private async Task PackAsync()
    {
        if (SelectedNode?.Kind != QueryNodeKind.FolderPackage)
            return;

        try
        {
            var sourcePath = SelectedNode.FullPath;
            await saveQueryByPathAsync(sourcePath);

            var zipPath = await QueryPathOperations.ResolvePackTargetAsync(getOwnerWindow(), sourcePath);
            if (string.IsNullOrEmpty(zipPath))
                return;

            StatusText = "Packing...";
            await QueryPathOperations.PackAsync(sourcePath, zipPath);
            await closeQueryTabsByPathAsync(sourcePath);
            QueryPathOperations.DeletePath(sourcePath);
            await openQueryAsync(zipPath);
            await ReloadDirectoryParentAsync(SelectedNode);
            StatusText = $"Packed: {Path.GetFileName(zipPath)}";
        }
        catch (Exception ex)
        {
            StatusText = $"Pack failed: {ex.Message}";
        }
    }

    private async Task UnpackAsync()
    {
        if (SelectedNode?.Kind != QueryNodeKind.PackageFile)
            return;

        try
        {
            var sourcePath = SelectedNode.FullPath;
            var folderPath = await QueryPathOperations.ResolveUnpackTargetAsync(getOwnerWindow(), sourcePath);
            if (string.IsNullOrEmpty(folderPath))
                return;

            StatusText = "Unpacking...";
            await QueryPathOperations.UnpackAsync(sourcePath, folderPath);
            await closeQueryTabsByPathAsync(sourcePath);
            QueryPathOperations.DeletePath(sourcePath);
            await openQueryAsync(folderPath);
            await ReloadDirectoryParentAsync(SelectedNode);
            StatusText = $"Unpacked: {Path.GetFileName(folderPath)}";
        }
        catch (Exception ex)
        {
            StatusText = $"Unpack failed: {ex.Message}";
        }
    }

    private QueryTreeNode CreateDirectoryNode(string path, bool isRoot = false)
    {
        var node = new QueryTreeNode
        {
            Name = isRoot ? Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                : Path.GetFileName(path),
            FullPath = path,
            Kind = QueryNodeKind.Directory,
            IsExpanded = isRoot,
            IsRoot = isRoot
        };

        if (isRoot && string.IsNullOrEmpty(node.Name))
            node.Name = path;

        node.LoadChildrenAsync = () => PopulateDirectoryAsync(node);
        return node;
    }

    private Task PopulateDirectoryAsync(QueryTreeNode node)
    {
        node.Children.Clear();

        if (!Directory.Exists(node.FullPath))
        {
            node.IsLoaded = true;
            return Task.CompletedTask;
        }

        var children = new List<QueryTreeNode>();

        foreach (var directory in Directory.EnumerateDirectories(node.FullPath))
        {
            var name = Path.GetFileName(directory);
            if (name.StartsWith('.') || string.Equals(name, ".lqpkg", StringComparison.OrdinalIgnoreCase))
                continue;

            if (PackageService.Instance.IsFolderPackage(directory))
            {
                children.Add(new QueryTreeNode
                {
                    Name = name,
                    FullPath = directory,
                    Kind = QueryNodeKind.FolderPackage,
                    IsLoaded = true
                });
                continue;
            }

            var child = CreateDirectoryNode(directory);
            child.Children.Add(new QueryTreeNode { IsPlaceholder = true });
            children.Add(child);
        }

        foreach (var file in Directory.EnumerateFiles(node.FullPath))
        {
            var name = Path.GetFileName(file);
            if (name.StartsWith('.'))
                continue;

            if (file.EndsWith(".lqpkg", StringComparison.OrdinalIgnoreCase))
            {
                children.Add(new QueryTreeNode
                {
                    Name = name,
                    FullPath = file,
                    Kind = QueryNodeKind.PackageFile,
                    IsLoaded = true
                });
            }
        }

        children.Sort(CompareTreeNodes);
        foreach (var child in children)
            node.Children.Add(child);

        node.IsLoaded = true;

        if (node.Kind == QueryNodeKind.Directory && node.Children.Count == 0)
            node.Children.Add(new QueryTreeNode { IsPlaceholder = true });

        return Task.CompletedTask;
    }

    private static bool IsFolderKind(QueryNodeKind kind) =>
        kind is QueryNodeKind.Directory or QueryNodeKind.FolderPackage;

    private static int CompareTreeNodes(QueryTreeNode a, QueryTreeNode b)
    {
        var aIsFolder = IsFolderKind(a.Kind);
        var bIsFolder = IsFolderKind(b.Kind);
        if (aIsFolder != bIsFolder)
            return aIsFolder ? -1 : 1;

        return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
    }

    private async Task ReloadDirectoryAtPathAsync(string directoryPath)
    {
        if (RootNode != null && string.Equals(RootNode.FullPath, directoryPath, StringComparison.OrdinalIgnoreCase))
        {
            await ReloadDirectoryNodeAsync(RootNode);
            return;
        }

        var node = FindDirectoryNode(RootNode, directoryPath);
        if (node != null)
            await ReloadDirectoryNodeAsync(node);
        else
            await RefreshAsync();
    }

    private async Task ReloadDirectoryParentAsync(QueryTreeNode node)
    {
        var parentPath = Path.GetDirectoryName(node.FullPath);
        if (string.IsNullOrEmpty(parentPath))
        {
            await RefreshAsync();
            return;
        }

        if (RootNode != null && string.Equals(RootNode.FullPath, parentPath, StringComparison.OrdinalIgnoreCase))
        {
            await ReloadDirectoryNodeAsync(RootNode);
            return;
        }

        var parent = FindDirectoryNode(RootNode, parentPath);
        if (parent != null)
            await ReloadDirectoryNodeAsync(parent);
        else
            await RefreshAsync();
    }

    private async Task ReloadDirectoryNodeAsync(QueryTreeNode? node)
    {
        if (node is null)
        {
            await RefreshAsync();
            return;
        }

        node.IsLoaded = false;
        if (node.LoadChildrenAsync is not null)
            await node.LoadChildrenAsync();
    }

    private static QueryTreeNode? FindDirectoryNode(QueryTreeNode? node, string path)
    {
        if (node is null)
            return null;

        if (node.Kind == QueryNodeKind.Directory
            && string.Equals(node.FullPath, path, StringComparison.OrdinalIgnoreCase))
            return node;

        foreach (var child in node.Children)
        {
            var found = FindDirectoryNode(child, path);
            if (found != null)
                return found;
        }

        return null;
    }

    private static void OpenInFileManager(string folderPath, string? selectPath)
    {
        if (OperatingSystem.IsWindows())
        {
            if (!string.IsNullOrEmpty(selectPath))
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{selectPath}\"") { UseShellExecute = true });
            else
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folderPath}\"") { UseShellExecute = true });
            return;
        }

        if (OperatingSystem.IsMacOS())
        {
            Process.Start(new ProcessStartInfo("open", folderPath) { UseShellExecute = true });
            return;
        }

        Process.Start(new ProcessStartInfo("xdg-open", folderPath) { UseShellExecute = true });
    }
}
