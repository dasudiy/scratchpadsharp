using System;
using Microsoft.Data.SqlClient;
using ScratchpadSharp.Shared.Models;

namespace ScratchpadSharp.Core.Database;

public readonly record struct DatabaseHostPort(string Host, int Port);

/// <summary>
/// Parses and rewrites networked database endpoints so an SSH local forward can
/// sit in front of SQL Server now, and other TCP providers later.
/// </summary>
public static class DatabaseEndpoint
{
    public static DatabaseHostPort Parse(string providerId, string connectionString)
    {
        var provider = DatabaseProviderCatalog.Get(providerId);
        if (!provider.SupportsSshTunnel)
            throw new InvalidOperationException($"{provider.DisplayName} connections do not use SSH tunnels.");

        return provider.Id switch
        {
            DatabaseProviderIds.SqlServer => ParseSqlServer(connectionString, provider.DefaultPort),
            _ => throw new NotSupportedException(
                $"SSH tunnel endpoint parsing is not implemented for {provider.DisplayName}.")
        };
    }

    public static string RewriteToLoopback(string providerId, string connectionString, int localPort)
    {
        if (localPort is <= 0 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(localPort), localPort, "Local port must be 1-65535.");

        var provider = DatabaseProviderCatalog.Get(providerId);
        return provider.Id switch
        {
            DatabaseProviderIds.SqlServer => RewriteSqlServer(connectionString, localPort),
            _ => throw new NotSupportedException(
                $"SSH tunnel connection rewriting is not implemented for {provider.DisplayName}.")
        };
    }

    private static DatabaseHostPort ParseSqlServer(string connectionString, int defaultPort)
    {
        var builder = new SqlConnectionStringBuilder(connectionString);
        var dataSource = (builder.DataSource ?? string.Empty).Trim();
        if (dataSource.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase))
            dataSource = dataSource[4..].Trim();

        if (string.IsNullOrWhiteSpace(dataSource))
            throw new InvalidOperationException("SQL Server connection string has no server address.");

        var comma = dataSource.LastIndexOf(',');
        if (comma >= 0 &&
            int.TryParse(dataSource[(comma + 1)..].Trim(), out var parsedPort) &&
            parsedPort is > 0 and <= 65535)
        {
            var host = dataSource[..comma].Trim();
            if (string.IsNullOrWhiteSpace(host))
                throw new InvalidOperationException("SQL Server connection string has no server address.");
            return new DatabaseHostPort(StripSqlInstanceName(host), parsedPort);
        }

        return new DatabaseHostPort(StripSqlInstanceName(dataSource), defaultPort > 0 ? defaultPort : 1433);
    }

    /// <summary>SSH forwards a TCP host, not a SQL Server named instance.</summary>
    private static string StripSqlInstanceName(string host)
    {
        var slash = host.IndexOf('\\');
        return slash >= 0 ? host[..slash] : host;
    }

    private static string RewriteSqlServer(string connectionString, int localPort)
    {
        var builder = new SqlConnectionStringBuilder(connectionString)
        {
            DataSource = $"127.0.0.1,{localPort}"
        };
        return builder.ConnectionString;
    }
}
