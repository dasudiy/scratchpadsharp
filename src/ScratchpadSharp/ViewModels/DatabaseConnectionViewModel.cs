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
using ScratchpadSharp.Core.Security;
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
    private bool sshEnabled;
    private string sshHost = string.Empty;
    private decimal sshPort = 22;
    private string sshUsername = string.Empty;
    private SshAuthMethodItem? selectedSshAuthMethod;
    private string sshPassword = string.Empty;
    private string sshPrivateKeyPath = string.Empty;
    private string sshPassphrase = string.Empty;
    private string sshRemoteHost = string.Empty;
    private decimal sshRemotePort;
    private decimal sshLocalPort;

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
            ConnectionStringText = RevealConnectionString(existing);
            SelectedProvider = DatabaseProviderCatalog.Get(existing.ProviderId);
            LoadSshTunnel(existing.SshTunnel);
        }
        else
        {
            SelectedProvider = DatabaseProviderCatalog.Get(DatabaseProviderIds.Sqlite);
            SelectedSshAuthMethod = SshAuthMethods[0];
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
            this.RaisePropertyChanged(nameof(SupportsSshTunnel));
        }
    }

    public bool SupportsSshTunnel => selectedProvider?.SupportsSshTunnel == true;

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
    public SshTunnelConfig? SavedSshTunnel { get; private set; }
    public bool WasSaved { get; private set; }

    public IReadOnlyList<SshAuthMethodItem> SshAuthMethods { get; } =
    [
        new(SshAuthMethod.Agent, "Agent"),
        new(SshAuthMethod.Password, "Password"),
        new(SshAuthMethod.PublicKey, "Public key")
    ];

    public bool SshEnabled
    {
        get => sshEnabled;
        set
        {
            this.RaiseAndSetIfChanged(ref sshEnabled, value);
            RaiseSshAuthVisibility();
        }
    }

    public string SshHost
    {
        get => sshHost;
        set => this.RaiseAndSetIfChanged(ref sshHost, value);
    }

    public decimal SshPort
    {
        get => sshPort;
        set => this.RaiseAndSetIfChanged(ref sshPort, value);
    }

    public string SshUsername
    {
        get => sshUsername;
        set => this.RaiseAndSetIfChanged(ref sshUsername, value);
    }

    public SshAuthMethodItem? SelectedSshAuthMethod
    {
        get => selectedSshAuthMethod;
        set
        {
            this.RaiseAndSetIfChanged(ref selectedSshAuthMethod, value);
            RaiseSshAuthVisibility();
        }
    }

    public string SshPassword
    {
        get => sshPassword;
        set => this.RaiseAndSetIfChanged(ref sshPassword, value);
    }

    public string SshPrivateKeyPath
    {
        get => sshPrivateKeyPath;
        set => this.RaiseAndSetIfChanged(ref sshPrivateKeyPath, value);
    }

    public string SshPassphrase
    {
        get => sshPassphrase;
        set => this.RaiseAndSetIfChanged(ref sshPassphrase, value);
    }

    public string SshRemoteHost
    {
        get => sshRemoteHost;
        set => this.RaiseAndSetIfChanged(ref sshRemoteHost, value);
    }

    public decimal SshRemotePort
    {
        get => sshRemotePort;
        set => this.RaiseAndSetIfChanged(ref sshRemotePort, value);
    }

    public decimal SshLocalPort
    {
        get => sshLocalPort;
        set => this.RaiseAndSetIfChanged(ref sshLocalPort, value);
    }

    public bool ShowSshPasswordFields =>
        SshEnabled && SelectedSshAuthMethod?.Value == SshAuthMethod.Password;

    public bool ShowSshPublicKeyFields =>
        SshEnabled && SelectedSshAuthMethod?.Value == SshAuthMethod.PublicKey;

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

    public async Task<bool> BrowsePrivateKeyAsync()
    {
        if (StorageProvider == null)
        {
            StatusText = "File picker is not available.";
            return false;
        }

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select SSH private key",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Private key") { Patterns = ["*.pem", "*.ppk", "*.key", "id_rsa", "id_ed25519", "id_ecdsa"] },
                new FilePickerFileType("All files") { Patterns = ["*"] }
            ]
        });

        var path = files.FirstOrDefault()?.Path.LocalPath;
        if (string.IsNullOrEmpty(path))
            return false;

        SshPrivateKeyPath = path;
        return true;
    }

    private string RevealConnectionString(ModuleInstanceConfig existing)
    {
        var cs = existing.ConnectionString;
        if (ModuleSecrets.TryRevealDatabasePassword(existing, out var password) && password.Length > 0)
            return ConnectionStringBuilderFactory.WithPassword(existing.ProviderId, cs, password);

        if (UserSecretProtector.IsProtected(existing.EncryptedDatabasePassword))
            StatusText = "Saved passwords could not be unlocked for this user on this machine. Re-enter them to continue.";
        return cs;
    }

    private void LoadSshTunnel(SshTunnelConfig? ssh)
    {
        SshEnabled = ssh?.Enabled == true;
        SshHost = ssh?.Host ?? string.Empty;
        SshPort = ssh is { Port: > 0 } ? ssh.Port : 22;
        SshUsername = ssh?.Username ?? string.Empty;
        SshPrivateKeyPath = ssh?.PrivateKeyPath ?? string.Empty;
        SshRemoteHost = ssh?.RemoteHost ?? string.Empty;
        SshRemotePort = ssh?.RemotePort ?? 0;
        SshLocalPort = ssh?.LocalPort ?? 0;
        var method = ssh?.AuthMethod ?? SshAuthMethod.Agent;
        SelectedSshAuthMethod = SshAuthMethods.FirstOrDefault(m => m.Value == method) ?? SshAuthMethods[0];

        if (ModuleSecrets.TryRevealSshPassword(ssh, out var password))
            SshPassword = password;
        else
        {
            SshPassword = string.Empty;
            StatusText = "Saved passwords could not be unlocked for this user on this machine. Re-enter them to continue.";
        }

        if (ModuleSecrets.TryRevealSshPassphrase(ssh, out var passphrase))
            SshPassphrase = passphrase;
        else
        {
            SshPassphrase = string.Empty;
            StatusText = "Saved passwords could not be unlocked for this user on this machine. Re-enter them to continue.";
        }
    }

    private SshTunnelConfig? BuildSshTunnelConfig()
    {
        if (!SupportsSshTunnel)
            return null;

        return new SshTunnelConfig
        {
            Enabled = SshEnabled,
            Host = SshHost.Trim(),
            Port = ToPort(SshPort, 22),
            Username = SshUsername.Trim(),
            AuthMethod = SelectedSshAuthMethod?.Value ?? SshAuthMethod.Agent,
            Password = SshPassword,
            PrivateKeyPath = SshPrivateKeyPath.Trim(),
            Passphrase = SshPassphrase,
            RemoteHost = SshRemoteHost.Trim(),
            RemotePort = ToPort(SshRemotePort, 0),
            LocalPort = ToPort(SshLocalPort, 0)
        };
    }

    private static int ToPort(decimal value, int fallback)
    {
        var port = (int)value;
        return port < 0 ? fallback : port;
    }

    private void RaiseSshAuthVisibility()
    {
        this.RaisePropertyChanged(nameof(ShowSshPasswordFields));
        this.RaisePropertyChanged(nameof(ShowSshPublicKeyFields));
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

        if (builder == null)
            return;

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
            var ssh = BuildSshTunnelConfig();
            if (ssh is { Enabled: true })
                SshTunnelSession.Validate(ssh);

            var result = await EfCoreModuleFactory.Instance.TestConnectionAsync(SelectedProvider.Id, cs, ssh);
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

        SshTunnelConfig? ssh = null;
        try
        {
            ssh = BuildSshTunnelConfig();
            if (ssh is { Enabled: true })
                SshTunnelSession.Validate(ssh);
        }
        catch (Exception ex)
        {
            ParseError = ex.Message;
            return;
        }

        SavedDisplayName = DisplayName.Trim();
        SavedProviderId = SelectedProvider.Id;
        SavedConnectionString = cs;
        SavedSshTunnel = ssh;
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

public sealed class SshAuthMethodItem
{
    public SshAuthMethodItem(SshAuthMethod value, string displayName)
    {
        Value = value;
        DisplayName = displayName;
    }

    public SshAuthMethod Value { get; }
    public string DisplayName { get; }
}
