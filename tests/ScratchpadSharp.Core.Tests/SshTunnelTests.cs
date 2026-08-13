using System.Text.Json;
using Microsoft.Data.SqlClient;
using ScratchpadSharp.Core.Database;
using ScratchpadSharp.Shared.Models;

namespace ScratchpadSharp.Core.Tests;

public static class SshTunnelTests
{
    public static int RunAll()
    {
        var failures = 0;
        failures += Run(nameof(SqlServer_Parse_HostOnly_DefaultsTo1433), SqlServer_Parse_HostOnly_DefaultsTo1433);
        failures += Run(nameof(SqlServer_Parse_HostAndPort), SqlServer_Parse_HostAndPort);
        failures += Run(nameof(SqlServer_Parse_TcpPrefix), SqlServer_Parse_TcpPrefix);
        failures += Run(nameof(SqlServer_Parse_NamedInstance_StripsInstanceForTcpHost), SqlServer_Parse_NamedInstance_StripsInstanceForTcpHost);
        failures += Run(nameof(SqlServer_Rewrite_UsesLoopbackAndKeepsCatalog), SqlServer_Rewrite_UsesLoopbackAndKeepsCatalog);
        failures += Run(nameof(Sqlite_DoesNotSupportSshTunnel), Sqlite_DoesNotSupportSshTunnel);
        failures += Run(nameof(ReplaceBakedConnectionString_RewritesOnConfiguring), ReplaceBakedConnectionString_RewritesOnConfiguring);
        failures += Run(nameof(Validate_RequiresHostUserAndAuthSecrets), Validate_RequiresHostUserAndAuthSecrets);
        failures += Run(nameof(ModuleConfig_SshTunnel_JsonRoundTrip), ModuleConfig_SshTunnel_JsonRoundTrip);
        return failures;
    }

    private static int Run(string name, Func<bool> test) =>
        test() ? 0 : ReportFail(name);

    private static int ReportFail(string name)
    {
        Console.WriteLine($"FAIL: {name}");
        return 1;
    }

    private static bool SqlServer_Parse_HostOnly_DefaultsTo1433()
    {
        var endpoint = DatabaseEndpoint.Parse(
            DatabaseProviderIds.SqlServer,
            "Server=db.internal;Database=app;TrustServerCertificate=True");
        return endpoint.Host == "db.internal" && endpoint.Port == 1433;
    }

    private static bool SqlServer_Parse_HostAndPort()
    {
        var endpoint = DatabaseEndpoint.Parse(
            DatabaseProviderIds.SqlServer,
            "Server=10.0.0.8,14333;Database=app");
        return endpoint.Host == "10.0.0.8" && endpoint.Port == 14333;
    }

    private static bool SqlServer_Parse_TcpPrefix()
    {
        var endpoint = DatabaseEndpoint.Parse(
            DatabaseProviderIds.SqlServer,
            "Data Source=tcp:sql01\\INST,41433;Initial Catalog=app");
        return endpoint.Host == "sql01" && endpoint.Port == 41433;
    }

    private static bool SqlServer_Parse_NamedInstance_StripsInstanceForTcpHost()
    {
        var endpoint = DatabaseEndpoint.Parse(
            DatabaseProviderIds.SqlServer,
            "Server=sql01\\INST;Database=app");
        return endpoint.Host == "sql01" && endpoint.Port == 1433;
    }

    private static bool SqlServer_Rewrite_UsesLoopbackAndKeepsCatalog()
    {
        var rewritten = DatabaseEndpoint.RewriteToLoopback(
            DatabaseProviderIds.SqlServer,
            "Server=db.internal,1433;Database=Sales;User ID=sa;Password=secret;TrustServerCertificate=True",
            51234);
        var sql = new SqlConnectionStringBuilder(rewritten);
        return sql.DataSource == "127.0.0.1,51234" &&
               sql.InitialCatalog == "Sales" &&
               sql.Password == "secret";
    }

    private static bool Sqlite_DoesNotSupportSshTunnel()
    {
        var provider = DatabaseProviderCatalog.Get(DatabaseProviderIds.Sqlite);
        if (provider.SupportsSshTunnel)
            return false;

        try
        {
            DatabaseEndpoint.Parse(DatabaseProviderIds.Sqlite, "Data Source=test.db");
            return false;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    private static bool ReplaceBakedConnectionString_RewritesOnConfiguring()
    {
        var original = "Server=db.internal;Database=app;TrustServerCertificate=True";
        var tunneled = "Data Source=127.0.0.1,51234;Initial Catalog=app;TrustServerCertificate=True";
        var snapshot = new DbSchemaSnapshot([
            new DbTableInfo("Orders", "dbo", false, [
                new DbColumnInfo("Id", "int", false, true, 0)
            ])
        ]);
        var provider = DatabaseProviderCatalog.Get(DatabaseProviderIds.SqlServer);
        var model = EfScaffoldGenerator.GenerateModel(snapshot, provider, "SalesDb", original);
        if (!model.Contains(EfScaffoldGenerator.EscapeCSharpString(original), StringComparison.Ordinal))
            return false;

        if (!EfScaffoldGenerator.TryReplaceBakedConnectionString(model, original, tunneled, out var rewritten))
            return false;

        return rewritten.Contains(EfScaffoldGenerator.EscapeCSharpString(tunneled), StringComparison.Ordinal) &&
               !rewritten.Contains(EfScaffoldGenerator.EscapeCSharpString(original), StringComparison.Ordinal);
    }

    private static bool Validate_RequiresHostUserAndAuthSecrets()
    {
        try
        {
            SshTunnelSession.Validate(new SshTunnelConfig { Enabled = true, Port = 22 });
            return false;
        }
        catch (InvalidOperationException) { /* expected */ }

        try
        {
            SshTunnelSession.Validate(new SshTunnelConfig
            {
                Enabled = true,
                Host = "bastion",
                Port = 22,
                Username = "ubuntu",
                AuthMethod = SshAuthMethod.Password
            });
            return false;
        }
        catch (InvalidOperationException) { /* expected */ }

        try
        {
            SshTunnelSession.Validate(new SshTunnelConfig
            {
                Enabled = true,
                Host = "bastion",
                Port = 22,
                Username = "ubuntu",
                AuthMethod = SshAuthMethod.PublicKey,
                PrivateKeyPath = "/tmp/does-not-exist-scratchpad-ssh-key"
            });
            return false;
        }
        catch (InvalidOperationException) { /* expected */ }

        SshTunnelSession.Validate(new SshTunnelConfig
        {
            Enabled = true,
            Host = "bastion",
            Port = 22,
            Username = "ubuntu",
            AuthMethod = SshAuthMethod.Agent
        });
        return true;
    }

    private static bool ModuleConfig_SshTunnel_JsonRoundTrip()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        var config = new ModuleInstanceConfig
        {
            Id = "abc",
            DisplayName = "Prod SQL",
            ProviderId = DatabaseProviderIds.SqlServer,
            ConnectionString = "Server=localhost;Database=app",
            SshTunnel = new SshTunnelConfig
            {
                Enabled = true,
                Host = "bastion.example",
                Port = 22,
                Username = "deploy",
                AuthMethod = SshAuthMethod.PublicKey,
                PrivateKeyPath = "/home/user/.ssh/id_ed25519"
            }
        };

        var json = JsonSerializer.Serialize(config, options);
        var round = JsonSerializer.Deserialize<ModuleInstanceConfig>(json, options);
        return json.Contains("\"sshTunnel\"", StringComparison.Ordinal) &&
               json.Contains("\"authMethod\": \"PublicKey\"", StringComparison.Ordinal) &&
               round?.SshTunnel is { Enabled: true, Host: "bastion.example", AuthMethod: SshAuthMethod.PublicKey };
    }
}
