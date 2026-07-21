using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Threading.Tasks;
using ReactiveUI;
using ScratchpadSharp.Core.Configuration;
using ScratchpadSharp.Core.Database;
using ScratchpadSharp.Core.PackageManagement;
using ScratchpadSharp.Shared.Models;

namespace ScratchpadSharp.ViewModels;

public class SchemaTreeNode : ReactiveObject
{
    public SchemaTreeNode(string title, string? subtitle = null, bool isTable = false, string? tableName = null)
    {
        Title = title;
        Subtitle = subtitle ?? string.Empty;
        IsTable = isTable;
        TableName = tableName;
    }

    public string Title { get; }
    public string Subtitle { get; }
    public bool IsTable { get; }
    public string? TableName { get; }
    public ObservableCollection<SchemaTreeNode> Children { get; } = new();

    public string DisplayText => string.IsNullOrEmpty(Subtitle) ? Title : $"{Title}  {Subtitle}";
}

public class DatabaseViewModel : ReactiveObject
{
    private readonly string tabId;
    private readonly ProjectContext projectContext;

    private DatabaseProviderInfo? selectedProvider;
    private string connectionString = string.Empty;
    private string statusText = string.Empty;
    private bool isBusy;
    private SchemaTreeNode? selectedNode;

    public DatabaseViewModel(string tabId, ProjectContext projectContext)
    {
        this.tabId = tabId;
        this.projectContext = projectContext;

        Providers = DatabaseProviderCatalog.All
            .Where(p => p.Id != DatabaseProviderIds.None)
            .ToList();

        SelectedProvider = DatabaseProviderCatalog.Get(
            DatabaseProviderCatalog.InferProviderId(projectContext.Config));
        if (SelectedProvider.Id == DatabaseProviderIds.None)
            SelectedProvider = DatabaseProviderCatalog.Get(DatabaseProviderIds.Sqlite);

        ConnectionString = ConfigurationLoader.ResolveConnectionString(projectContext.Config);

        TestConnectionCommand = ReactiveCommand.CreateFromTask(TestConnectionAsync,
            this.WhenAnyValue(x => x.IsBusy, busy => !busy));
        RefreshSchemaCommand = ReactiveCommand.CreateFromTask(RefreshSchemaAsync,
            this.WhenAnyValue(x => x.IsBusy, busy => !busy));
        ApplyToQueryCommand = ReactiveCommand.CreateFromTask(ApplyToQueryAsync,
            this.WhenAnyValue(x => x.IsBusy, busy => !busy));
        CloseCommand = ReactiveCommand.Create(() => { });
    }

    public System.Collections.Generic.IReadOnlyList<DatabaseProviderInfo> Providers { get; }

    public ObservableCollection<SchemaTreeNode> SchemaRoots { get; } = new();

    public DatabaseProviderInfo? SelectedProvider
    {
        get => selectedProvider;
        set
        {
            var previous = selectedProvider;
            this.RaiseAndSetIfChanged(ref selectedProvider, value);
            if (value != null && previous != null &&
                !string.Equals(previous.Id, value.Id, StringComparison.OrdinalIgnoreCase))
            {
                var cs = ConnectionString ?? string.Empty;
                if (string.IsNullOrWhiteSpace(cs) ||
                    string.Equals(cs, previous.ConnectionStringTemplate, StringComparison.Ordinal))
                    ConnectionString = value.ConnectionStringTemplate;
            }
        }
    }

    public string ConnectionString
    {
        get => connectionString;
        set => this.RaiseAndSetIfChanged(ref connectionString, value);
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

    public SchemaTreeNode? SelectedNode
    {
        get => selectedNode;
        set => this.RaiseAndSetIfChanged(ref selectedNode, value);
    }

    public ReactiveCommand<Unit, Unit> TestConnectionCommand { get; }
    public ReactiveCommand<Unit, Unit> RefreshSchemaCommand { get; }
    public ReactiveCommand<Unit, Unit> ApplyToQueryCommand { get; }
    public ReactiveCommand<Unit, Unit> CloseCommand { get; }

    private IDbSchemaProvider? CreateProvider()
    {
        var provider = DbSchemaProviderFactory.Create(SelectedProvider?.Id);
        if (provider == null)
            StatusText = "Select SQLite or SQL Server to inspect a database.";
        return provider;
    }

    private async Task TestConnectionAsync()
    {
        var provider = CreateProvider();
        if (provider == null) return;

        IsBusy = true;
        try
        {
            StatusText = "Testing connection...";
            var result = await provider.TestConnectionAsync(ConnectionString);
            StatusText = result.Success
                ? $"OK ({result.ElapsedMilliseconds} ms)" +
                  (string.IsNullOrEmpty(result.ServerVersion) ? "" : $" — {result.ServerVersion}")
                : $"Failed ({result.ElapsedMilliseconds} ms): {result.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RefreshSchemaAsync()
    {
        var provider = CreateProvider();
        if (provider == null) return;

        IsBusy = true;
        try
        {
            StatusText = "Loading schema...";
            var snapshot = await provider.GetSchemaAsync(ConnectionString);
            SchemaRoots.Clear();

            var tablesRoot = new SchemaTreeNode(
                string.IsNullOrEmpty(snapshot.DatabaseName) ? "Tables" : snapshot.DatabaseName);

            foreach (var table in snapshot.Tables)
            {
                var label = table.IsView ? $"view {table.Name}" : table.Name;
                if (!string.IsNullOrEmpty(table.Schema) &&
                    !table.Schema.Equals("main", StringComparison.OrdinalIgnoreCase) &&
                    !table.Schema.Equals("dbo", StringComparison.OrdinalIgnoreCase))
                    label = $"{table.Schema}.{label}";

                var tableNode = new SchemaTreeNode(label, $"{table.Columns.Count} cols",
                    isTable: true, tableName: table.Name);

                foreach (var col in table.Columns)
                {
                    var flags = col.IsPrimaryKey ? " PK" : "";
                    var nullability = col.IsNullable ? " null" : " not null";
                    tableNode.Children.Add(new SchemaTreeNode(
                        col.Name, $"{col.DataType}{nullability}{flags}"));
                }

                tablesRoot.Children.Add(tableNode);
            }

            SchemaRoots.Add(tablesRoot);
            StatusText = $"Loaded {snapshot.Tables.Count} table(s)/view(s).";
        }
        catch (Exception ex)
        {
            SchemaRoots.Clear();
            StatusText = $"Schema failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ApplyToQueryAsync()
    {
        if (SelectedProvider == null) return;

        IsBusy = true;
        try
        {
            StatusText = "Applying provider and connection string to query...";
            projectContext.Config.ConnectionString = ConnectionString ?? string.Empty;
            await ProjectService.Instance.SetDatabaseProviderAsync(tabId, projectContext, SelectedProvider.Id);
            // SetDatabaseProvider may rewrite CS from template — keep user's typed value.
            projectContext.Config.ConnectionString = ConnectionString ?? string.Empty;
            StatusText = "Applied to in-memory config. Save the query to persist config.json.";
        }
        catch (Exception ex)
        {
            StatusText = $"Apply failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
