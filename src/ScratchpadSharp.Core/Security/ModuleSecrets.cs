using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ScratchpadSharp.Core.Modules;
using ScratchpadSharp.Shared.Models;

namespace ScratchpadSharp.Core.Security;

public static class ModuleSecrets
{
    public static void ProtectInPlace(ModuleInstanceConfig config)
    {
        ProtectDatabasePassword(config);
        ProtectSshSecrets(config);
    }

    public static bool TryRevealDatabasePassword(ModuleInstanceConfig config, out string password) =>
        UserSecretProtector.TryUnprotect(config.EncryptedDatabasePassword, out password) &&
        password.Length > 0;

    public static bool TryRevealSshPassword(SshTunnelConfig? ssh, out string password)
    {
        password = string.Empty;
        if (ssh == null || string.IsNullOrEmpty(ssh.Password))
            return true;
        return UserSecretProtector.TryUnprotect(ssh.Password, out password);
    }

    public static bool TryRevealSshPassphrase(SshTunnelConfig? ssh, out string passphrase)
    {
        passphrase = string.Empty;
        if (ssh == null || string.IsNullOrEmpty(ssh.Passphrase))
            return true;
        return UserSecretProtector.TryUnprotect(ssh.Passphrase, out passphrase);
    }

    public static async Task<ModuleInstanceConfig> UnlockAsync(
        ModuleInstanceConfig stored, CancellationToken ct = default)
    {
        var live = Clone(stored);
        live.ConnectionString = await ResolveDatabasePasswordAsync(stored, live.ConnectionString, ct);
        if (live.SshTunnel != null)
        {
            live.SshTunnel = live.SshTunnel.Clone();
            await ResolveSshSecretsAsync(stored, live.SshTunnel, ct);
        }

        return live;
    }

    private static void ProtectDatabasePassword(ModuleInstanceConfig config)
    {
        if (ConnectionStringBuilderFactory.UsesIntegratedSecurity(config.ProviderId, config.ConnectionString))
        {
            config.EncryptedDatabasePassword = null;
            config.ConnectionString = ConnectionStringBuilderFactory.WithPassword(
                config.ProviderId, config.ConnectionString, string.Empty);
            return;
        }

        var password = ConnectionStringBuilderFactory.GetPassword(config.ProviderId, config.ConnectionString);
        if (!string.IsNullOrEmpty(password))
        {
            config.EncryptedDatabasePassword = UserSecretProtector.Protect(password);
            config.ConnectionString = ConnectionStringBuilderFactory.WithPassword(
                config.ProviderId, config.ConnectionString, string.Empty);
            return;
        }

        config.ConnectionString = ConnectionStringBuilderFactory.WithPassword(
            config.ProviderId, config.ConnectionString, string.Empty);

        if (!ConnectionStringBuilderFactory.RequiresSqlAuthPassword(config.ProviderId, config.ConnectionString))
            config.EncryptedDatabasePassword = null;
    }

    private static void ProtectSshSecrets(ModuleInstanceConfig config)
    {
        var ssh = config.SshTunnel;
        if (ssh == null)
            return;

        switch (ssh.AuthMethod)
        {
            case SshAuthMethod.Password:
                ssh.Password = string.IsNullOrEmpty(ssh.Password)
                    ? ssh.Password
                    : UserSecretProtector.Protect(ssh.Password);
                ssh.Passphrase = string.Empty;
                break;
            case SshAuthMethod.PublicKey:
                ssh.Password = string.Empty;
                ssh.Passphrase = string.IsNullOrEmpty(ssh.Passphrase)
                    ? ssh.Passphrase
                    : UserSecretProtector.Protect(ssh.Passphrase);
                break;
            default:
                ssh.Password = string.Empty;
                ssh.Passphrase = string.Empty;
                break;
        }
    }

    private static async Task<string> ResolveDatabasePasswordAsync(
        ModuleInstanceConfig stored, string connectionString, CancellationToken ct)
    {
        if (!NeedsDatabasePassword(stored, connectionString))
            return ConnectionStringBuilderFactory.WithPassword(stored.ProviderId, connectionString, string.Empty);

        if (TryRevealDatabasePassword(stored, out var password))
            return ConnectionStringBuilderFactory.WithPassword(stored.ProviderId, connectionString, password);

        var entered = await PromptAsync(stored, UserSecretKind.DatabasePassword, ct);
        stored.EncryptedDatabasePassword = UserSecretProtector.Protect(entered);
        stored.ConnectionString = ConnectionStringBuilderFactory.WithPassword(
            stored.ProviderId, stored.ConnectionString, string.Empty);
        Persist(stored);
        return ConnectionStringBuilderFactory.WithPassword(stored.ProviderId, connectionString, entered);
    }

    private static bool NeedsDatabasePassword(ModuleInstanceConfig stored, string connectionString)
    {
        if (ConnectionStringBuilderFactory.UsesIntegratedSecurity(stored.ProviderId, connectionString))
            return false;
        return !string.IsNullOrEmpty(stored.EncryptedDatabasePassword) ||
               ConnectionStringBuilderFactory.RequiresSqlAuthPassword(stored.ProviderId, connectionString);
    }

    private static async Task ResolveSshSecretsAsync(
        ModuleInstanceConfig stored, SshTunnelConfig liveSsh, CancellationToken ct)
    {
        if (!liveSsh.Enabled)
            return;

        if (liveSsh.AuthMethod == SshAuthMethod.Password)
        {
            if (!TryRevealSshPassword(liveSsh, out var password) || password.Length == 0)
            {
                password = await PromptAsync(stored, UserSecretKind.SshPassword, ct);
                if (stored.SshTunnel != null)
                {
                    stored.SshTunnel.Password = UserSecretProtector.Protect(password);
                    Persist(stored);
                }
            }

            liveSsh.Password = password;
        }
        else if (liveSsh.AuthMethod == SshAuthMethod.PublicKey &&
                 !string.IsNullOrEmpty(liveSsh.Passphrase))
        {
            if (!TryRevealSshPassphrase(liveSsh, out var passphrase) || passphrase.Length == 0)
            {
                passphrase = await PromptAsync(stored, UserSecretKind.SshPassphrase, ct);
                if (stored.SshTunnel != null)
                {
                    stored.SshTunnel.Passphrase = UserSecretProtector.Protect(passphrase);
                    Persist(stored);
                }
            }

            liveSsh.Passphrase = passphrase;
        }
    }

    private static async Task<string> PromptAsync(
        ModuleInstanceConfig stored, UserSecretKind kind, CancellationToken ct)
    {
        var prompt = UserSecretPrompt.Current;
        if (prompt == null)
        {
            throw new InvalidOperationException(
                $"{KindLabel(kind)} is not available for this user on this machine. Re-enter it in the connection dialog.");
        }

        var entered = await prompt.RequestAsync(
            new UserSecretPromptRequest(stored.Id, stored.DisplayName, kind), ct);
        if (string.IsNullOrEmpty(entered))
            throw new InvalidOperationException($"{KindLabel(kind)} entry was cancelled.");

        return entered;
    }

    private static void Persist(ModuleInstanceConfig stored)
    {
        if (string.IsNullOrEmpty(stored.Id))
            return;

        var model = ModuleCatalog.Instance.ReadModelSource(stored.Id) ?? string.Empty;
        ModuleCatalog.Instance.Save(stored, model);
    }

    private static string KindLabel(UserSecretKind kind) => kind switch
    {
        UserSecretKind.DatabasePassword => "Database password",
        UserSecretKind.SshPassword => "SSH password",
        UserSecretKind.SshPassphrase => "SSH key passphrase",
        _ => "Password"
    };

    private static ModuleInstanceConfig Clone(ModuleInstanceConfig config) => new()
    {
        Id = config.Id,
        TypeId = config.TypeId,
        DisplayName = config.DisplayName,
        NamespaceSegment = config.NamespaceSegment,
        ProviderId = config.ProviderId,
        ConnectionString = config.ConnectionString,
        EncryptedDatabasePassword = config.EncryptedDatabasePassword,
        SshTunnel = config.SshTunnel?.Clone(),
        Usings = [..config.Usings],
        NuGetPackages = new Dictionary<string, string>(config.NuGetPackages)
    };
}
