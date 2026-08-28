using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Threading.Tasks;
using ReactiveUI;
using ScratchpadSharp.Core.Configuration;
using ScratchpadSharp.Core.Storage;

namespace ScratchpadSharp.ViewModels;

public enum QueryNodeKind
{
    Directory,
    ScriptFile,
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
    private QueryTreeNode? rootNode;
    private string statusText = string.Empty;
    private QueryTreeNode? selectedNode;

    public QueriesSidebarViewModel(Func<string, Task> openQueryAsync)
    {
        this.openQueryAsync = openQueryAsync;

        RefreshCommand = ReactiveCommand.CreateFromTask(RefreshAsync);
        OpenSelectedCommand = ReactiveCommand.CreateFromTask(OpenSelectedAsync,
            this.WhenAnyValue(x => x.SelectedNode, node => node is { IsPlaceholder: false }
                && node.Kind != QueryNodeKind.Directory));

        ApplicationSettings.Changed += OnSettingsChanged;
        _ = RefreshAsync();
    }

    public QueryTreeNode? RootNode
    {
        get => rootNode;
        private set => this.RaiseAndSetIfChanged(ref rootNode, value);
    }

    public QueryTreeNode? SelectedNode
    {
        get => selectedNode;
        set => this.RaiseAndSetIfChanged(ref selectedNode, value);
    }

    public string StatusText
    {
        get => statusText;
        set => this.RaiseAndSetIfChanged(ref statusText, value);
    }

    public ReactiveCommand<Unit, Unit> RefreshCommand { get; }
    public ReactiveCommand<Unit, Unit> OpenSelectedCommand { get; }

    public void RequestRefresh() => _ = RefreshAsync();

    public Task OpenSelectedAsync()
    {
        if (SelectedNode is not { IsPlaceholder: false } node || node.Kind == QueryNodeKind.Directory)
            return Task.CompletedTask;

        return openQueryAsync(node.FullPath);
    }

    private void OnSettingsChanged() => _ = RefreshAsync();

    private async Task RefreshAsync()
    {
        try
        {
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

    private QueryTreeNode CreateDirectoryNode(string path, bool isRoot = false)
    {
        var node = new QueryTreeNode
        {
            Name = isRoot ? Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                : Path.GetFileName(path),
            FullPath = path,
            Kind = QueryNodeKind.Directory,
            IsExpanded = isRoot
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

        foreach (var directory in Directory.EnumerateDirectories(node.FullPath).OrderBy(Path.GetFileName))
        {
            var name = Path.GetFileName(directory);
            if (name.StartsWith('.'))
                continue;

            if (PackageService.Instance.IsFolderPackage(directory))
            {
                node.Children.Add(new QueryTreeNode
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
            node.Children.Add(child);
        }

        foreach (var file in Directory.EnumerateFiles(node.FullPath).OrderBy(Path.GetFileName))
        {
            var name = Path.GetFileName(file);
            if (name.StartsWith('.'))
                continue;

            if (file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                node.Children.Add(new QueryTreeNode
                {
                    Name = name,
                    FullPath = file,
                    Kind = QueryNodeKind.ScriptFile,
                    IsLoaded = true
                });
            }
            else if (file.EndsWith(".lqpkg", StringComparison.OrdinalIgnoreCase))
            {
                node.Children.Add(new QueryTreeNode
                {
                    Name = name,
                    FullPath = file,
                    Kind = QueryNodeKind.PackageFile,
                    IsLoaded = true
                });
            }
        }

        node.IsLoaded = true;
        return Task.CompletedTask;
    }
}
