using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Text;
using System.Threading.Tasks;
using ReactiveUI;
using ScratchpadSharp.Core.Configuration;
using ScratchpadSharp.Core.Database;
using ScratchpadSharp.Core.PackageManagement;
using ScratchpadSharp.Shared.Models;

namespace ScratchpadSharp.ViewModels;

public class SchemaTreeNode : ReactiveObject
{
    public SchemaTreeNode(string title, string? subtitle = null, bool isTable = false,
        string? tableName = null, DbTableInfo? table = null)
    {
        Title = title;
        Subtitle = subtitle ?? string.Empty;
        IsTable = isTable;
        TableName = tableName;
        Table = table;
    }

    public string Title { get; }
    public string Subtitle { get; }
    public bool IsTable { get; }
    public string? TableName { get; }
    public DbTableInfo? Table { get; }
    public SchemaTreeNode? Parent { get; set; }
    public ObservableCollection<SchemaTreeNode> Children { get; } = new();

    public string DisplayText => string.IsNullOrEmpty(Subtitle) ? Title : $"{Title}  {Subtitle}";
}

public class DatabaseViewModel : ReactiveObject
{
    private readonly string tabId;
    private readonly ProjectContext projectContext;
    private readonly Action<string> insertIntoScript;
    private DbSchemaSnapshot? lastSnapshot;

    private DatabaseProviderInfo? selectedProvider;
    private string connectionString = string.Empty;
    private string statusText = string.Empty;
    private bool isBusy;
    private SchemaTreeNode? selectedNode;
    private string sqlText = "SELECT 1;";
    private string sqlResultText = string.Empty;

    public DatabaseViewModel(string tabId, ProjectContext projectContext, Action<string> insertIntoScript)
    {
        this.tabId = tabId;
        this.projectContext = projectContext;
        this.insertIntoScript = insertIntoScript;

        Providers = DatabaseProviderCatalog.All
            .Where(p => p.Id != DatabaseProviderIds.None)
            .ToList();

        SelectedProvider = DatabaseProviderCatalog.Get(
            DatabaseProviderCatalog.InferProviderId(projectContext.Config));
        if (SelectedProvider.Id == DatabaseProviderIds.None)
            SelectedProvider = DatabaseProviderCatalog.Get(DatabaseProviderIds.Sqlite);

        ConnectionString = ConfigurationLoader.ResolveConnectionString(projectContext.Config);

        var canRun = this.WhenAnyValue(x => x.IsBusy, busy => !busy);
        TestConnectionCommand = ReactiveCommand.CreateFromTask(TestConnectionAsync, canRun);
        RefreshSchemaCommand = ReactiveCommand.CreateFromTask(RefreshSchemaAsync, canRun);
        ApplyToQueryCommand = ReactiveCommand.CreateFromTask(ApplyToQueryAsync, canRun);
        ScaffoldSelectedCommand = ReactiveCommand.Create(ScaffoldSelected, canRun);
        ScaffoldAllCommand = ReactiveCommand.Create(ScaffoldAll, canRun);
        ExecuteSqlCommand = ReactiveCommand.CreateFromTask(ExecuteSqlAsync, canRun);
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

    public string SqlText
    {
        get => sqlText;
        set => this.RaiseAndSetIfChanged(ref sqlText, value);
    }

    public string SqlResultText
    {
        get => sqlResultText;
        set => this.RaiseAndSetIfChanged(ref sqlResultText, value);
    }

    public ReactiveCommand<Unit, Unit> TestConnectionCommand { get; }
    public ReactiveCommand<Unit, Unit> RefreshSchemaCommand { get; }
    public ReactiveCommand<Unit, Unit> ApplyToQueryCommand { get; }
    public ReactiveCommand<Unit, Unit> ScaffoldSelectedCommand { get; }
    public ReactiveCommand<Unit, Unit> ScaffoldAllCommand { get; }
    public ReactiveCommand<Unit, Unit> ExecuteSqlCommand { get; }
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
            lastSnapshot = await provider.GetSchemaAsync(ConnectionString);
            SchemaRoots.Clear();

            var tablesRoot = new SchemaTreeNode(
                string.IsNullOrEmpty(lastSnapshot.DatabaseName) ? "Tables" : lastSnapshot.DatabaseName);

            foreach (var table in lastSnapshot.Tables)
            {
                var label = table.IsView ? $"view {table.Name}" : table.Name;
                if (!string.IsNullOrEmpty(table.Schema) &&
                    !table.Schema.Equals("main", StringComparison.OrdinalIgnoreCase) &&
                    !table.Schema.Equals("dbo", StringComparison.OrdinalIgnoreCase))
                    label = $"{table.Schema}.{label}";

                var tableNode = new SchemaTreeNode(label, $"{table.Columns.Count} cols",
                    isTable: true, tableName: table.Name, table: table)
                {
                    Parent = tablesRoot
                };

                foreach (var col in table.Columns)
                {
                    var flags = col.IsPrimaryKey ? " PK" : "";
                    var nullability = col.IsNullable ? " null" : " not null";
                    tableNode.Children.Add(new SchemaTreeNode(
                        col.Name, $"{col.DataType}{nullability}{flags}")
                    {
                        Parent = tableNode
                    });
                }

                tablesRoot.Children.Add(tableNode);
            }

            SchemaRoots.Add(tablesRoot);
            StatusText = $"Loaded {lastSnapshot.Tables.Count} table(s)/view(s).";
        }
        catch (Exception ex)
        {
            lastSnapshot = null;
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

    private void ScaffoldSelected()
    {
        if (SelectedProvider == null)
            return;

        if (lastSnapshot == null)
        {
            StatusText = "Refresh schema first.";
            return;
        }

        var tableName = SelectedNode?.TableName
                        ?? SelectedNode?.Table?.Name
                        ?? FindTableName(SelectedNode);
        if (string.IsNullOrEmpty(tableName))
        {
            StatusText = "Select a table in the schema tree.";
            return;
        }

        var code = EfScaffoldGenerator.Generate(lastSnapshot, SelectedProvider, [tableName]);
        insertIntoScript(code);
        StatusText = $"Inserted scaffold for {tableName} into the script editor.";
    }

    private void ScaffoldAll()
    {
        if (SelectedProvider == null)
            return;

        if (lastSnapshot == null)
        {
            StatusText = "Refresh schema first.";
            return;
        }

        var code = EfScaffoldGenerator.Generate(lastSnapshot, SelectedProvider);
        insertIntoScript(code);
        StatusText = "Inserted scaffold for all tables into the script editor.";
    }

    private static string? FindTableName(SchemaTreeNode? node)
    {
        for (var current = node; current != null; current = current.Parent)
        {
            if (current.IsTable && !string.IsNullOrEmpty(current.TableName))
                return current.TableName;
        }

        return null;
    }

    private async Task ExecuteSqlAsync()
    {
        var provider = CreateProvider();
        if (provider == null) return;

        IsBusy = true;
        try
        {
            StatusText = "Executing SQL...";
            var result = await provider.ExecuteQueryAsync(ConnectionString, SqlText);
            SqlResultText = FormatQueryResult(result);
            StatusText = $"SQL returned {result.Rows.Count} row(s), {result.Columns.Count} column(s).";
        }
        catch (Exception ex)
        {
            SqlResultText = string.Empty;
            StatusText = $"SQL failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string FormatQueryResult(DbQueryResult result)
    {
        if (result.Columns.Count == 0)
            return "(no result set)";

        var sb = new StringBuilder();
        sb.AppendLine(string.Join('\t', result.Columns));
        foreach (var row in result.Rows)
            sb.AppendLine(string.Join('\t', row.Select(v => v ?? "NULL")));
        return sb.ToString();
    }
}
