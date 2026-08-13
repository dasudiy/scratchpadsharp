using Microsoft.Data.SqlClient;
using ScratchpadSharp.Core.Database;
using ScratchpadSharp.Core.Modules;
using ScratchpadSharp.Core.Security;
using ScratchpadSharp.Shared.Models;

namespace ScratchpadSharp.Core.Tests;

public static class UserSecretProtectorTests
{
    public static int RunAll()
    {
        var failures = 0;
        failures += Run(nameof(Protect_RoundTrip), Protect_RoundTrip);
        failures += Run(nameof(Protect_IsIdempotent), Protect_IsIdempotent);
        failures += Run(nameof(Unprotect_CorruptBlob_Fails), Unprotect_CorruptBlob_Fails);
        failures += Run(nameof(Unprotect_Unprefixed_Fails), Unprotect_Unprefixed_Fails);
        failures += Run(nameof(ProtectInPlace_StripsSqlPassword), ProtectInPlace_StripsSqlPassword);
        failures += Run(nameof(ProtectInPlace_ClearsPassword_OnWindowsAuth), ProtectInPlace_ClearsPassword_OnWindowsAuth);
        failures += Run(nameof(Unlock_WindowsAuth_DoesNotInjectEncryptedPassword), Unlock_WindowsAuth_DoesNotInjectEncryptedPassword);
        failures += Run(nameof(Unlock_InjectsSqlAndSshSecrets), Unlock_InjectsSqlAndSshSecrets);
        failures += Run(nameof(Unlock_PromptsWhenBlobCannotBeOpened), Unlock_PromptsWhenBlobCannotBeOpened);
        failures += Run(nameof(Unlock_HeadlessWithoutPrompt_FailsClearly), Unlock_HeadlessWithoutPrompt_FailsClearly);
        failures += Run(nameof(SqlAuth_RequiresPassword_WhenUserIdSet), SqlAuth_RequiresPassword_WhenUserIdSet);
        return failures;
    }

    private static int Run(string name, Func<bool> test) =>
        test() ? 0 : ReportFail(name);

    private static int ReportFail(string name)
    {
        Console.WriteLine($"FAIL: {name}");
        return 1;
    }

    private static bool Protect_RoundTrip()
    {
        var sealedValue = UserSecretProtector.Protect("s3cret");
        return UserSecretProtector.IsProtected(sealedValue) &&
               UserSecretProtector.TryUnprotect(sealedValue, out var plain) &&
               plain == "s3cret";
    }

    private static bool Protect_IsIdempotent()
    {
        var once = UserSecretProtector.Protect("pw");
        var twice = UserSecretProtector.Protect(once);
        return once == twice && UserSecretProtector.TryUnprotect(twice, out var plain) && plain == "pw";
    }

    private static bool Unprotect_CorruptBlob_Fails()
    {
        var sealedValue = UserSecretProtector.Protect("pw");
        var corrupt = sealedValue[..^2] + "xx";
        return !UserSecretProtector.TryUnprotect(corrupt, out var plain) && plain == string.Empty;
    }

    private static bool Unprotect_Unprefixed_Fails() =>
        !UserSecretProtector.TryUnprotect("not-encrypted", out var plain) && plain == string.Empty;

    private static bool ProtectInPlace_StripsSqlPassword()
    {
        var config = new ModuleInstanceConfig
        {
            ProviderId = DatabaseProviderIds.SqlServer,
            ConnectionString = "Server=db;Database=app;User ID=sa;Password=db-secret;TrustServerCertificate=True",
            SshTunnel = new SshTunnelConfig
            {
                Enabled = true,
                Host = "bastion",
                Username = "deploy",
                AuthMethod = SshAuthMethod.Password,
                Password = "ssh-secret"
            }
        };

        ModuleSecrets.ProtectInPlace(config);
        var sql = new SqlConnectionStringBuilder(config.ConnectionString);
        return string.IsNullOrEmpty(sql.Password) &&
               UserSecretProtector.IsProtected(config.EncryptedDatabasePassword) &&
               UserSecretProtector.IsProtected(config.SshTunnel?.Password) &&
               !config.ConnectionString.Contains("db-secret", StringComparison.Ordinal) &&
               config.SshTunnel!.Password != "ssh-secret";
    }

    private static bool ProtectInPlace_ClearsPassword_OnWindowsAuth()
    {
        var config = new ModuleInstanceConfig
        {
            ProviderId = DatabaseProviderIds.SqlServer,
            ConnectionString = "Server=db;Database=app;Integrated Security=True;User ID=sa;Password=stale;TrustServerCertificate=True",
            EncryptedDatabasePassword = UserSecretProtector.Protect("stale")
        };

        ModuleSecrets.ProtectInPlace(config);
        return config.EncryptedDatabasePassword == null &&
               ConnectionStringBuilderFactory.GetPassword(config.ProviderId, config.ConnectionString).Length == 0;
    }

    private static bool Unlock_WindowsAuth_DoesNotInjectEncryptedPassword()
    {
        var previous = UserSecretPrompt.Current;
        UserSecretPrompt.Current = null;
        try
        {
            var config = new ModuleInstanceConfig
            {
                DisplayName = "Sales",
                ProviderId = DatabaseProviderIds.SqlServer,
                ConnectionString = "Server=db;Database=app;Integrated Security=True;TrustServerCertificate=True",
                EncryptedDatabasePassword = UserSecretProtector.Protect("stale")
            };

            var live = ModuleSecrets.UnlockAsync(config).GetAwaiter().GetResult();
            return string.IsNullOrEmpty(new SqlConnectionStringBuilder(live.ConnectionString).Password);
        }
        finally
        {
            UserSecretPrompt.Current = previous;
        }
    }

    private static bool Unlock_InjectsSqlAndSshSecrets()
    {
        var previous = UserSecretPrompt.Current;
        UserSecretPrompt.Current = null;
        try
        {
            var config = new ModuleInstanceConfig
            {
                Id = "mod1",
                DisplayName = "Sales",
                ProviderId = DatabaseProviderIds.SqlServer,
                ConnectionString = "Server=db;Database=app;User ID=sa;Password=db-secret;TrustServerCertificate=True",
                SshTunnel = new SshTunnelConfig
                {
                    Enabled = true,
                    Host = "bastion",
                    Username = "deploy",
                    AuthMethod = SshAuthMethod.Password,
                    Password = "ssh-secret"
                }
            };
            ModuleSecrets.ProtectInPlace(config);

            var live = ModuleSecrets.UnlockAsync(config).GetAwaiter().GetResult();
            var sql = new SqlConnectionStringBuilder(live.ConnectionString);
            return sql.Password == "db-secret" &&
                   live.SshTunnel?.Password == "ssh-secret" &&
                   string.IsNullOrEmpty(new SqlConnectionStringBuilder(config.ConnectionString).Password);
        }
        finally
        {
            UserSecretPrompt.Current = previous;
        }
    }

    private static bool Unlock_PromptsWhenBlobCannotBeOpened()
    {
        var previous = UserSecretPrompt.Current;
        var prompt = new FakePrompt { Value = "retyped" };
        UserSecretPrompt.Current = prompt;
        try
        {
            var config = new ModuleInstanceConfig
            {
                Id = string.Empty,
                DisplayName = "Sales",
                ProviderId = DatabaseProviderIds.SqlServer,
                ConnectionString = "Server=db;Database=app;User ID=sa;TrustServerCertificate=True",
                EncryptedDatabasePassword = UserSecretProtector.Prefix + Convert.ToBase64String("not-valid-cipher"u8.ToArray())
            };

            var live = ModuleSecrets.UnlockAsync(config).GetAwaiter().GetResult();
            var sql = new SqlConnectionStringBuilder(live.ConnectionString);
            return prompt.Calls == 1 && sql.Password == "retyped";
        }
        finally
        {
            UserSecretPrompt.Current = previous;
        }
    }

    private static bool Unlock_HeadlessWithoutPrompt_FailsClearly()
    {
        var previous = UserSecretPrompt.Current;
        UserSecretPrompt.Current = null;
        try
        {
            var config = new ModuleInstanceConfig
            {
                DisplayName = "Sales",
                ProviderId = DatabaseProviderIds.SqlServer,
                ConnectionString = "Server=db;Database=app;User ID=sa;TrustServerCertificate=True",
                EncryptedDatabasePassword = UserSecretProtector.Prefix + Convert.ToBase64String("not-valid-cipher"u8.ToArray())
            };

            try
            {
                ModuleSecrets.UnlockAsync(config).GetAwaiter().GetResult();
                return false;
            }
            catch (InvalidOperationException ex)
            {
                return ex.Message.Contains("Database password", StringComparison.Ordinal);
            }
        }
        finally
        {
            UserSecretPrompt.Current = previous;
        }
    }

    private static bool SqlAuth_RequiresPassword_WhenUserIdSet()
    {
        var needs = ConnectionStringBuilderFactory.RequiresSqlAuthPassword(
            DatabaseProviderIds.SqlServer,
            "Server=db;Database=app;User ID=sa;TrustServerCertificate=True");
        var windows = ConnectionStringBuilderFactory.RequiresSqlAuthPassword(
            DatabaseProviderIds.SqlServer,
            "Server=db;Database=app;Integrated Security=True;TrustServerCertificate=True");
        var stripped = ConnectionStringBuilderFactory.WithPassword(
            DatabaseProviderIds.SqlServer,
            "Server=db;User ID=sa;Password=hidden",
            string.Empty);
        var restored = ConnectionStringBuilderFactory.WithPassword(
            DatabaseProviderIds.SqlServer, stripped, "hidden");
        return needs && !windows &&
               ConnectionStringBuilderFactory.GetPassword(DatabaseProviderIds.SqlServer, stripped).Length == 0 &&
               ConnectionStringBuilderFactory.GetPassword(DatabaseProviderIds.SqlServer, restored) == "hidden";
    }

    private sealed class FakePrompt : IUserSecretPrompt
    {
        public string? Value { get; set; }
        public int Calls { get; private set; }

        public Task<string?> RequestAsync(UserSecretPromptRequest request, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(Value);
        }
    }
}
