using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data.Common;
using System.Linq;
using System.Reactive;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using ReactiveUI;
using ScratchpadSharp.Core.Database;
using ScratchpadSharp.Core.Modules;
using ScratchpadSharp.Shared.Models;

namespace ScratchpadSharp.ViewModels;

public class DatabaseConnectionViewModel : ReactiveObject
{
    private string displayName = string.Empty;
    private DatabaseProviderInfo? selectedProvider;
    private bool useFormMode = true;
    private string connectionStringText = string.Empty;
    private string statusText = string.Empty;
    private string parseError = string.Empty;
    private bool isBusy;
    private bool suppressConnectionStringSideEffects;
    private DbConnectionStringBuilder? builder;

    public DatabaseConnectionViewModel(ModuleInstanceConfig? existing = null)
    {
        IsEdit = existing != null;
        ExistingId = existing?.Id;

        Providers = DatabaseProviderCatalog.SelectableProviders.ToList();
        CommonFields = new ObservableCollection<ConnectionStringFieldDescriptor>();
        AdvancedFields = new ObservableCollection<ConnectionStringFieldDescriptor>();

        if (existing != null)
        {
            DisplayName = existing.DisplayName;
            ConnectionStringText = existing.ConnectionString;
            SelectedProvider = DatabaseProviderCatalog.Get(existing.ProviderId);
        }
        else
        {
            SelectedProvider = DatabaseProviderCatalog.Get(DatabaseProviderIds.Sqlite);
        }

        TestConnectionCommand = ReactiveCommand.CreateFromTask(TestConnectionAsync,
            this.WhenAnyValue(x => x.IsBusy, busy => !busy));
        SaveCommand = ReactiveCommand.Create(Save,
            this.WhenAnyValue(x => x.IsBusy, x => x.DisplayName, (busy, name) => !busy && !string.IsNullOrWhiteSpace(name)));
        CancelCommand = ReactiveCommand.Create(() => { });
    }

    public bool IsEdit { get; }
    public string? ExistingId { get; }
    public IReadOnlyList<DatabaseProviderInfo> Providers { get; }
    public ObservableCollection<ConnectionStringFieldDescriptor> CommonFields { get; }
    public ObservableCollection<ConnectionStringFieldDescriptor> AdvancedFields { get; }

    public IStorageProvider? StorageProvider { get; set; }

    public string DisplayName
    {
        get => displayName;
        set => this.RaiseAndSetIfChanged(ref displayName, value);
    }

    public DatabaseProviderInfo? SelectedProvider
    {
        get => selectedProvider;
        set
        {
            var previousId = selectedProvider?.Id;
            this.RaiseAndSetIfChanged(ref selectedProvider, value);
            if (value == null)
                return;

            var providerChanged = previousId != null &&
                                  !string.Equals(previousId, value.Id, StringComparison.OrdinalIgnoreCase);
            RebuildBuilderFromProvider(providerChanged);
        }
    }

    public bool UseFormMode
    {
        get => useFormMode;
        set
        {
            if (useFormMode == value)
                return;

            if (value)
                SyncFormFromConnectionString();
            else
                SyncConnectionStringFromForm();

            this.RaiseAndSetIfChanged(ref useFormMode, value);
            this.RaisePropertyChanged(nameof(UseConnectionStringMode));
        }
    }

    public bool UseConnectionStringMode
    {
        get => !useFormMode;
        set
        {
            if (value == !useFormMode)
                return;
            UseFormMode = !value;
        }
    }

    public string ConnectionStringText
    {
        get => connectionStringText;
        set
        {
            this.RaiseAndSetIfChanged(ref connectionStringText, value);
            if (suppressConnectionStringSideEffects || UseFormMode || SelectedProvider == null)
                return;

            if (ConnectionStringBuilderFactory.TryParseConnectionString(
                    SelectedProvider.Id, value, out var parsed, out var error))
            {
                builder = parsed;
                ParseError = string.Empty;
            }
            else
                ParseError = error ?? "Invalid connection string.";
        }
    }

    public string StatusText
    {
        get => statusText;
        set => this.RaiseAndSetIfChanged(ref statusText, value);
    }

    public string ParseError
    {
        get => parseError;
        set => this.RaiseAndSetIfChanged(ref parseError, value);
    }

    public bool IsBusy
    {
        get => isBusy;
        set => this.RaiseAndSetIfChanged(ref isBusy, value);
    }

    public ReactiveCommand<Unit, Unit> TestConnectionCommand { get; }
    public ReactiveCommand<Unit, Unit> SaveCommand { get; }
    public ReactiveCommand<Unit, Unit> CancelCommand { get; }

    public string? SavedDisplayName { get; private set; }
    public string? SavedProviderId { get; private set; }
    public string? SavedConnectionString { get; private set; }
    public bool WasSaved { get; private set; }

    public void OnFieldChanged(ConnectionStringFieldDescriptor field)
    {
        if (builder == null)
            return;

        ConnectionStringBuilderFactory.ApplyFields(builder, CommonFields.Concat(AdvancedFields));
        suppressConnectionStringSideEffects = true;
        ConnectionStringText = builder.ConnectionString;
        suppressConnectionStringSideEffects = false;
        ParseError = string.Empty;
    }

    public void SetBuilderFieldValue(string key, object? value)
    {
        if (builder == null || SelectedProvider == null)
            return;

        var props = TypeDescriptor.GetProperties(builder);
        var pd = props[key];
        if (pd == null)
            return;

        pd.SetValue(builder, value);
        suppressConnectionStringSideEffects = true;
        ConnectionStringText = builder.ConnectionString;
        suppressConnectionStringSideEffects = false;
        RefreshFieldsFromBuilder();
        ParseError = string.Empty;
    }

    public async Task<bool> BrowseDatabaseFileAsync(ConnectionStringFieldDescriptor field)
    {
        if (StorageProvider == null)
        {
            StatusText = "File picker is not available.";
            return false;
        }

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select SQLite database file",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("SQLite database") { Patterns = ["*.db", "*.sqlite", "*.sqlite3"] },
                new FilePickerFileType("All files") { Patterns = ["*"] }
            ]
        });

        var path = files.FirstOrDefault()?.Path.LocalPath;
        if (string.IsNullOrEmpty(path))
            return false;

        SetBuilderFieldValue(field.Key, path);
        return true;
    }

    private void SyncConnectionStringFromForm()
    {
        if (builder == null)
            return;

        ConnectionStringBuilderFactory.ApplyFields(builder, CommonFields.Concat(AdvancedFields));
        suppressConnectionStringSideEffects = true;
        ConnectionStringText = builder.ConnectionString;
        suppressConnectionStringSideEffects = false;
        ParseError = string.Empty;
    }

    private void SyncFormFromConnectionString()
    {
        if (SelectedProvider == null)
            return;

        if (string.IsNullOrWhiteSpace(ConnectionStringText))
        {
            RefreshFieldsFromBuilder();
            return;
        }

        if (ConnectionStringBuilderFactory.TryParseConnectionString(
                SelectedProvider.Id, ConnectionStringText, out var parsed, out var error))
        {
            builder = parsed;
            ParseError = string.Empty;
            RefreshFieldsFromBuilder();
        }
        else
            ParseError = error ?? "Invalid connection string.";
    }

    private void RebuildBuilderFromProvider(bool providerChanged)
    {
        if (SelectedProvider == null)
            return;

        if (providerChanged)
        {
            builder = ConnectionStringBuilderFactory.CreateEmpty(SelectedProvider.Id);
            if (!string.IsNullOrEmpty(SelectedProvider.ConnectionStringTemplate))
                builder.ConnectionString = SelectedProvider.ConnectionStringTemplate;
        }
        else
        {
            var seed = ConnectionStringText;
            if (string.IsNullOrWhiteSpace(seed) && !string.IsNullOrEmpty(SelectedProvider.ConnectionStringTemplate))
                seed = SelectedProvider.ConnectionStringTemplate;

            if (!string.IsNullOrWhiteSpace(seed) &&
                ConnectionStringBuilderFactory.TryParseConnectionString(SelectedProvider.Id, seed, out var parsed, out _))
                builder = parsed;
            else
                builder = ConnectionStringBuilderFactory.CreateEmpty(SelectedProvider.Id);
        }

        suppressConnectionStringSideEffects = true;
        ConnectionStringText = builder.ConnectionString;
        suppressConnectionStringSideEffects = false;

        if (UseFormMode)
            RefreshFieldsFromBuilder();

        ParseError = string.Empty;
        StatusText = string.Empty;
    }

    private void RefreshFieldsFromBuilder()
    {
        if (builder == null || SelectedProvider == null)
            return;

        var fields = ConnectionStringBuilderFactory.GetFields(SelectedProvider.Id, builder);
        CommonFields.Clear();
        AdvancedFields.Clear();
        foreach (var field in fields.Where(f => f.IsCommon))
            CommonFields.Add(field);
        foreach (var field in fields.Where(f => !f.IsCommon))
            AdvancedFields.Add(field);
    }

    private async Task TestConnectionAsync()
    {
        if (SelectedProvider == null)
            return;

        IsBusy = true;
        StatusText = "Testing connection...";
        try
        {
            var cs = GetEffectiveConnectionString();
            var result = await EfCoreModuleFactory.Instance.TestConnectionAsync(SelectedProvider.Id, cs);
            StatusText = result.Success
                ? $"Connected ({result.ElapsedMilliseconds} ms)"
                : $"Failed: {result.Message}";
        }
        catch (Exception ex)
        {
            StatusText = $"Failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Save()
    {
        if (SelectedProvider == null)
            return;

        var cs = GetEffectiveConnectionString();
        if (string.IsNullOrWhiteSpace(cs))
        {
            ParseError = "Connection string is required.";
            return;
        }

        SavedDisplayName = DisplayName.Trim();
        SavedProviderId = SelectedProvider.Id;
        SavedConnectionString = cs;
        WasSaved = true;
    }

    private string GetEffectiveConnectionString()
    {
        if (UseFormMode && builder != null)
        {
            ConnectionStringBuilderFactory.ApplyFields(builder, CommonFields.Concat(AdvancedFields));
            return builder.ConnectionString;
        }

        return ConnectionStringText ?? string.Empty;
    }
}
