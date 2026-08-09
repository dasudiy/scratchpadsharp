using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Threading.Tasks;
using ReactiveUI;
using ScratchpadSharp.Core.Modules;
using ScratchpadSharp.Core.PackageManagement;
using ScratchpadSharp.Shared.Models;

namespace ScratchpadSharp.ViewModels;

public class ModuleTreeNode : ReactiveObject
{
    private bool isExpanded;
    private bool isLoading;

    public string Name { get; set; } = string.Empty;
    public string NodeKind { get; set; } = string.Empty;
    public string? InstanceId { get; set; }
    public string? TableName { get; set; }

    public bool IsExpanded
    {
        get => isExpanded;
        set => this.RaiseAndSetIfChanged(ref isExpanded, value);
    }

    public bool IsLoading
    {
        get => isLoading;
        set => this.RaiseAndSetIfChanged(ref isLoading, value);
    }

    public ObservableCollection<ModuleTreeNode> Children { get; } = new();
}

public class ModulesSidebarViewModel : ReactiveObject
{
    private readonly Func<ScriptTabViewModel?> getSelectedTab;
    private readonly Func<string, string, string, Task> openModuleQueryAsync;
    private string statusText = string.Empty;
    private bool isBusy;
    private ModuleTreeNode? selectedNode;

    public ModulesSidebarViewModel(
        Func<ScriptTabViewModel?> getSelectedTab,
        Func<string, string, string, Task> openModuleQueryAsync)
    {
        this.getSelectedTab = getSelectedTab;
        this.openModuleQueryAsync = openModuleQueryAsync;
        RootNodes = new ObservableCollection<ModuleTreeNode>();

        RefreshCommand = ReactiveCommand.CreateFromTask(RefreshAsync);
        AddDatabaseCommand = ReactiveCommand.CreateFromTask(AddDatabaseAsync,
            this.WhenAnyValue(x => x.IsBusy, busy => !busy));
        EditConnectionCommand = ReactiveCommand.CreateFromTask(EditConnectionAsync,
            this.WhenAnyValue(x => x.SelectedNode, x => x.IsBusy,
                (node, busy) => !busy && node?.NodeKind == "Instance"));
        DeleteInstanceCommand = ReactiveCommand.CreateFromTask(DeleteInstanceAsync,
            this.WhenAnyValue(x => x.SelectedNode, x => x.IsBusy,
                (node, busy) => !busy && node?.NodeKind == "Instance"));
        RegenerateModelCommand = ReactiveCommand.CreateFromTask(RegenerateModelAsync,
            this.WhenAnyValue(x => x.SelectedNode, x => x.IsBusy,
                (node, busy) => !busy && node?.NodeKind == "Instance" && node is { IsLoading: false }));
        AddRefCommand = ReactiveCommand.CreateFromTask(AddRefAsync,
            this.WhenAnyValue(x => x.SelectedNode, x => x.IsBusy,
                (node, busy) => !busy && node?.NodeKind == "Instance"));
        RemoveRefCommand = ReactiveCommand.CreateFromTask(RemoveRefAsync,
            this.WhenAnyValue(x => x.SelectedNode, x => x.IsBusy,
                (node, busy) => !busy && node?.NodeKind == "Instance"));
        Take100Command = ReactiveCommand.CreateFromTask(Take100Async,
            this.WhenAnyValue(x => x.SelectedNode, x => x.IsBusy,
                (node, busy) => !busy && node?.NodeKind == "Table"));
        CountCommand = ReactiveCommand.CreateFromTask(CountAsync,
            this.WhenAnyValue(x => x.SelectedNode, x => x.IsBusy,
                (node, busy) => !busy && node?.NodeKind == "Table"));

        _ = RefreshAsync();
    }

    public ObservableCollection<ModuleTreeNode> RootNodes { get; }

    public ModuleTreeNode? SelectedNode
    {
        get => selectedNode;
        set => this.RaiseAndSetIfChanged(ref selectedNode, value);
    }

    public string StatusText
    {
        get => statusText;
        set => this.RaiseAndSetIfChanged(ref statusText, value);
    }

    public bool IsBusy
    {
        get => isBusy;
        set => this.RaiseAndSetIfChanged(ref isBusy, value);
    }

    public ReactiveCommand<Unit, Unit> RefreshCommand { get; }
    public ReactiveCommand<Unit, Unit> AddDatabaseCommand { get; }
    public ReactiveCommand<Unit, Unit> EditConnectionCommand { get; }
    public ReactiveCommand<Unit, Unit> DeleteInstanceCommand { get; }
    public ReactiveCommand<Unit, Unit> RegenerateModelCommand { get; }
    public ReactiveCommand<Unit, Unit> AddRefCommand { get; }
    public ReactiveCommand<Unit, Unit> RemoveRefCommand { get; }
    public ReactiveCommand<Unit, Unit> Take100Command { get; }
    public ReactiveCommand<Unit, Unit> CountCommand { get; }

    public async Task RefreshAsync()
    {
        IsBusy = true;

        var efSection = RootNodes.FirstOrDefault(n => n.NodeKind == "Type");
        if (efSection == null)
        {
            efSection = new ModuleTreeNode
            {
                Name = "EF Core",
                NodeKind = "Type",
                IsExpanded = true
            };
            RootNodes.Add(efSection);
        }

        efSection.IsLoading = true;
        efSection.Children.Clear();
        efSection.Children.Add(CreateLoadingNode("Loading modules..."));

        try
        {
            efSection.Children.Clear();

            foreach (var instance in ModuleCatalog.Instance.ListInstances(ModuleTypeIds.EfCore))
            {
                var instanceNode = new ModuleTreeNode
                {
                    Name = instance.DisplayName,
                    NodeKind = "Instance",
                    InstanceId = instance.Id,
                    IsExpanded = false
                };

                await PopulateInstanceChildrenAsync(instanceNode, instance.Id);
                efSection.Children.Add(instanceNode);
            }

            StatusText = $"{efSection.Children.Count} database(s)";
        }
        catch (Exception ex)
        {
            efSection.Children.Clear();
            efSection.Children.Add(new ModuleTreeNode
            {
                Name = $"Refresh failed: {ex.Message}",
                NodeKind = "Error"
            });
            StatusText = $"Refresh failed: {ex.Message}";
        }
        finally
        {
            efSection.IsLoading = false;
            IsBusy = false;
        }
    }

    private static ModuleTreeNode CreateLoadingNode(string message) =>
        new()
        {
            Name = message,
            NodeKind = "Loading",
            IsLoading = true
        };

    private async Task PopulateInstanceChildrenAsync(ModuleTreeNode instanceNode, string instanceId)
    {
        instanceNode.Children.Clear();

        try
        {
            var snapshot = await EfCoreModuleFactory.Instance.GetSchemaAsync(instanceId);
            foreach (var table in snapshot.Tables)
            {
                var tableNode = new ModuleTreeNode
                {
                    Name = table.IsView ? $"{table.Name} (view)" : table.Name,
                    NodeKind = "Table",
                    InstanceId = instanceId,
                    TableName = table.Name
                };

                foreach (var col in table.Columns)
                {
                    tableNode.Children.Add(new ModuleTreeNode
                    {
                        Name = $"{col.Name} ({col.DataType})",
                        NodeKind = "Column"
                    });
                }

                instanceNode.Children.Add(tableNode);
            }
        }
        catch (Exception ex)
        {
            instanceNode.Children.Add(new ModuleTreeNode
            {
                Name = $"Schema error: {ex.Message}",
                NodeKind = "Error"
            });
        }
    }

    private ModuleTreeNode? FindInstanceNode(string? instanceId)
    {
        if (instanceId == null)
            return null;

        foreach (var root in RootNodes)
        {
            foreach (var child in root.Children)
            {
                if (child.NodeKind == "Instance" && child.InstanceId == instanceId)
                    return child;
            }
        }

        return null;
    }

    private void SetInstanceLoadingState(ModuleTreeNode instanceNode, bool loading)
    {
        instanceNode.IsLoading = loading;
        if (!loading)
            return;

        instanceNode.IsExpanded = true;
        instanceNode.Children.Clear();
        instanceNode.Children.Add(CreateLoadingNode("Loading schema..."));
    }

    private async Task AddDatabaseAsync()
    {
        var dialog = await ShowConnectionDialogAsync(null);
        if (dialog?.WasSaved != true)
            return;

        IsBusy = true;
        StatusText = "Creating module...";
        try
        {
            await EfCoreModuleFactory.Instance.CreateInstanceAsync(
                dialog.SavedDisplayName!,
                dialog.SavedProviderId!,
                dialog.SavedConnectionString!);
            await RefreshAsync();
            StatusText = $"Added {dialog.SavedDisplayName}";
        }
        catch (Exception ex)
        {
            StatusText = $"Create failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task EditConnectionAsync()
    {
        var instanceId = SelectedNode?.InstanceId;
        if (instanceId == null)
            return;

        var existing = ModuleCatalog.Instance.TryGet(instanceId);
        if (existing == null)
            return;

        var dialog = await ShowConnectionDialogAsync(existing);
        if (dialog?.WasSaved != true)
            return;

        IsBusy = true;
        try
        {
            await EfCoreModuleFactory.Instance.UpdateConnectionAsync(
                existing,
                dialog.SavedProviderId!,
                dialog.SavedConnectionString!);
            existing.DisplayName = dialog.SavedDisplayName!;
            ModuleCatalog.Instance.Save(existing, ModuleCatalog.Instance.ReadModelSource(instanceId) ?? string.Empty);
            await RefreshAsync();
            StatusText = $"Updated {existing.DisplayName}";
        }
        catch (Exception ex)
        {
            StatusText = $"Update failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private Task DeleteInstanceAsync()
    {
        var instanceId = SelectedNode?.InstanceId;
        if (instanceId == null)
            return Task.CompletedTask;

        ModuleCatalog.Instance.Delete(instanceId);

        var efSection = RootNodes.FirstOrDefault(n => n.NodeKind == "Type");
        var instanceNode = FindInstanceNode(instanceId);
        if (instanceNode != null)
            efSection?.Children.Remove(instanceNode);

        StatusText = efSection != null
            ? $"{efSection.Children.Count} database(s)"
            : "Module deleted";

        return Task.CompletedTask;
    }

    private async Task RegenerateModelAsync()
    {
        var instanceId = SelectedNode?.InstanceId;
        if (instanceId == null)
            return;

        var instanceNode = FindInstanceNode(instanceId);
        if (instanceNode == null)
            return;

        SetInstanceLoadingState(instanceNode, loading: true);
        StatusText = "Regenerating model...";
        try
        {
            await EfCoreModuleFactory.Instance.RegenerateModelAsync(instanceId);
            await PopulateInstanceChildrenAsync(instanceNode, instanceId);
            StatusText = "Model regenerated";
        }
        catch (Exception ex)
        {
            instanceNode.Children.Clear();
            instanceNode.Children.Add(new ModuleTreeNode
            {
                Name = $"Regenerate failed: {ex.Message}",
                NodeKind = "Error"
            });
            StatusText = $"Regenerate failed: {ex.Message}";
        }
        finally
        {
            instanceNode.IsLoading = false;
        }
    }

    private async Task AddRefAsync()
    {
        var tab = getSelectedTab();
        if (tab == null || SelectedNode?.InstanceId == null)
        {
            StatusText = "Select a query tab first";
            return;
        }

        await ProjectService.Instance.AddModuleRefAsync(tab.TabId, tab.ProjectContext, SelectedNode.InstanceId);
        tab.RaisePropertyChanged(nameof(ScriptTabViewModel.ProjectContext));
        StatusText = "Module reference added to query";
    }

    private async Task RemoveRefAsync()
    {
        var tab = getSelectedTab();
        if (tab == null || SelectedNode?.InstanceId == null)
        {
            StatusText = "Select a query tab first";
            return;
        }

        await ProjectService.Instance.RemoveModuleRefAsync(tab.TabId, tab.ProjectContext, SelectedNode.InstanceId);
        tab.RaisePropertyChanged(nameof(ScriptTabViewModel.ProjectContext));
        StatusText = "Module reference removed";
    }

    private async Task Take100Async() => await RunTableScriptAsync(100);
    private async Task CountAsync() => await RunTableScriptAsync(0, countOnly: true);

    private async Task RunTableScriptAsync(int take, bool countOnly = false)
    {
        if (SelectedNode?.InstanceId == null || SelectedNode.TableName == null)
            return;

        var config = ModuleCatalog.Instance.TryGet(SelectedNode.InstanceId);
        if (config == null)
            return;

        var tableName = SelectedNode.TableName;
        var script = countOnly
            ? EfCoreModuleFactory.Instance.BuildCountScript(config, tableName)
            : EfCoreModuleFactory.Instance.BuildTakeScript(config, tableName, take);

        var title = countOnly
            ? $"{tableName} — Count"
            : $"{tableName} — Take({take})";

        StatusText = countOnly ? "Opening count query..." : $"Opening take({take}) query...";
        try
        {
            await openModuleQueryAsync(config.Id, title, script);
            StatusText = countOnly ? "Count query executed" : $"Take({take}) query executed";
        }
        catch (Exception ex)
        {
            StatusText = $"Query failed: {ex.Message}";
        }
    }

    private async Task<DatabaseConnectionViewModel?> ShowConnectionDialogAsync(ModuleInstanceConfig? existing)
    {
        var main = Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
        var owner = main?.MainWindow;
        if (owner == null)
            return null;

        var vm = new DatabaseConnectionViewModel(existing);
        var window = new Views.DatabaseConnectionWindow { DataContext = vm };
        await window.ShowDialog(owner);
        return vm.WasSaved ? vm : null;
    }
}
