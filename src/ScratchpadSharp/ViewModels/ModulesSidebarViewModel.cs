using System;
using System.IO;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Threading;
using System.Threading.Tasks;
using ReactiveUI;
using ScratchpadSharp.Core.Configuration;
using ScratchpadSharp.Core.Database;
using ScratchpadSharp.Core.Modules;
using ScratchpadSharp.Core.PackageManagement;
using ScratchpadSharp.Core.Services;
using ScratchpadSharp.Services;
using ScratchpadSharp.Shared.Models;

namespace ScratchpadSharp.ViewModels;

public class ModuleTreeNode : ReactiveObject
{
    public string Name { get; set; } = string.Empty;
    public string NodeKind { get; set; } = string.Empty;
    public string? InstanceId { get; set; }
    public string? TableName { get; set; }
    public bool IsExpanded { get; set; }
    public ObservableCollection<ModuleTreeNode> Children { get; } = new();
}

public class ModulesSidebarViewModel : ReactiveObject
{
    private readonly Func<ScriptTabViewModel?> getSelectedTab;
    private readonly IScriptExecutionService scriptService;
    private string statusText = string.Empty;
    private bool isBusy;
    private ModuleTreeNode? selectedNode;

    public ModulesSidebarViewModel(
        Func<ScriptTabViewModel?> getSelectedTab,
        IScriptExecutionService scriptService)
    {
        this.getSelectedTab = getSelectedTab;
        this.scriptService = scriptService;
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
                (node, busy) => !busy && node?.NodeKind == "Instance"));
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
        try
        {
            RootNodes.Clear();
            var efSection = new ModuleTreeNode { Name = "EF Core", NodeKind = "Type" };
            foreach (var instance in ModuleCatalog.Instance.ListInstances(ModuleTypeIds.EfCore))
            {
                var instanceNode = new ModuleTreeNode
                {
                    Name = instance.DisplayName,
                    NodeKind = "Instance",
                    InstanceId = instance.Id,
                    IsExpanded = false
                };

                try
                {
                    var snapshot = await EfCoreModuleFactory.Instance.GetSchemaAsync(instance.Id);
                    foreach (var table in snapshot.Tables)
                    {
                        var tableNode = new ModuleTreeNode
                        {
                            Name = table.IsView ? $"{table.Name} (view)" : table.Name,
                            NodeKind = "Table",
                            InstanceId = instance.Id,
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

                efSection.Children.Add(instanceNode);
            }

            RootNodes.Add(efSection);
            StatusText = $"{efSection.Children.Count} database(s)";
        }
        catch (Exception ex)
        {
            StatusText = $"Refresh failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
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

    private async Task DeleteInstanceAsync()
    {
        var instanceId = SelectedNode?.InstanceId;
        if (instanceId == null)
            return;

        ModuleCatalog.Instance.Delete(instanceId);
        await RefreshAsync();
        StatusText = "Module deleted";
    }

    private async Task RegenerateModelAsync()
    {
        var instanceId = SelectedNode?.InstanceId;
        if (instanceId == null)
            return;

        IsBusy = true;
        StatusText = "Regenerating model...";
        try
        {
            await EfCoreModuleFactory.Instance.RegenerateModelAsync(instanceId);
            await RefreshAsync();
            StatusText = "Model regenerated";
        }
        catch (Exception ex)
        {
            StatusText = $"Regenerate failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
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

        var script = countOnly
            ? EfCoreModuleFactory.Instance.BuildCountScript(config, SelectedNode.TableName)
            : EfCoreModuleFactory.Instance.BuildTakeScript(config, SelectedNode.TableName, take);

        IsBusy = true;
        StatusText = "Running...";
        try
        {
            await RunEphemeralScriptAsync(config.Id, script);
        }
        catch (Exception ex)
        {
            StatusText = $"Run failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RunEphemeralScriptAsync(string instanceId, string code)
    {
        var tab = getSelectedTab();
        var ephemeralId = Guid.NewGuid().ToString("N");
        var context = new ProjectContext
        {
            EffectiveRootPath = Path.GetTempPath(),
            Config = new ScriptConfig
            {
                ModuleRefs = [instanceId],
                TimeoutSeconds = ApplicationSettings.DefaultTimeoutSeconds
            },
            Manifest = new PackageManifest()
        };

        await ProjectService.Instance.RefreshMergedEnvironmentAsync(ephemeralId, context);

        if (tab != null)
        {
            tab.StatusText = "Running module script...";
            var htmlDump = new HtmlDumpService();
            var result = await scriptService.ExecuteAsync(code, context, htmlDump.DumpSink);
            tab.Output = result.Success
                ? $"{result.Output}\n{htmlDump.TextOutput}".Trim()
                : $"Error: {result.ErrorMessage}\n{result.Output}";
            tab.StatusText = result.Success ? "Module script completed" : "Module script failed";
            StatusText = tab.StatusText;
        }
        else
        {
            var htmlDump = new HtmlDumpService();
            var result = await scriptService.ExecuteAsync(code, context, htmlDump.DumpSink);
            StatusText = result.Success ? "Completed (no query tab — see dump in status)" : $"Failed: {result.ErrorMessage}";
        }

        RoslynWorkspaceService.Instance.RemoveProject(ephemeralId);
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
