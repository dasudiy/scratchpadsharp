using System;
using System.Collections.Generic;
using System.Linq;
using ScratchpadSharp.Shared.Models;

namespace ScratchpadSharp.Core.Database;

public sealed record DatabaseProviderInfo(
    string Id,
    string DisplayName,
    string? EfProviderPackageId,
    string EfProviderPackageVersion,
    string UseExtensionMethod,
    string ConnectionStringTemplate);

public static class DatabaseProviderCatalog
{
    public const string EfCorePackageId = "Microsoft.EntityFrameworkCore";
    public const string EfCorePackageVersion = "8.0.11";

    private static readonly HashSet<string> ProviderPackageIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "Microsoft.EntityFrameworkCore.Sqlite",
        "Microsoft.EntityFrameworkCore.SqlServer",
        "Npgsql.EntityFrameworkCore.PostgreSQL",
        "Pomelo.EntityFrameworkCore.MySql"
    };

    public static IReadOnlyList<DatabaseProviderInfo> All { get; } =
    [
        new(DatabaseProviderIds.None, "None", null, "", "", ""),
        new(DatabaseProviderIds.Sqlite, "SQLite",
            "Microsoft.EntityFrameworkCore.Sqlite", EfCorePackageVersion,
            "UseSqlite", "Data Source=scratchpad.db"),
        new(DatabaseProviderIds.SqlServer, "SQL Server",
            "Microsoft.EntityFrameworkCore.SqlServer", EfCorePackageVersion,
            "UseSqlServer",
            "Server=localhost;Database=Scratchpad;Trusted_Connection=True;TrustServerCertificate=True")
    ];

    public static DatabaseProviderInfo Get(string? id)
    {
        var normalized = string.IsNullOrWhiteSpace(id) ? DatabaseProviderIds.None : id.Trim();
        return All.FirstOrDefault(p =>
                   p.Id.Equals(normalized, StringComparison.OrdinalIgnoreCase))
               ?? All[0];
    }

    public static string InferProviderId(ScriptConfig config)
    {
        if (!string.IsNullOrWhiteSpace(config.DatabaseProvider))
            return Get(config.DatabaseProvider).Id;

        foreach (var provider in All.Where(p => p.EfProviderPackageId != null))
        {
            if (config.NuGetPackages.ContainsKey(provider.EfProviderPackageId!))
                return provider.Id;
        }

        if (config.NuGetPackages.ContainsKey(EfCorePackageId))
            return DatabaseProviderIds.Sqlite;

        return DatabaseProviderIds.None;
    }

    /// <summary>
    /// Updates <see cref="ScriptConfig.DatabaseProvider"/> and EF-related NuGet packages.
    /// Does not resolve/download packages — caller must call ProjectService resolve.
    /// </summary>
    public static void ApplyToConfig(ScriptConfig config, string providerId)
    {
        var previous = Get(InferProviderId(config));
        var provider = Get(providerId);
        config.DatabaseProvider = provider.Id;

        foreach (var id in ProviderPackageIds.ToList())
            config.NuGetPackages.Remove(id);

        if (provider.Id == DatabaseProviderIds.None)
        {
            config.NuGetPackages.Remove(EfCorePackageId);
            return;
        }

        config.NuGetPackages[EfCorePackageId] = EfCorePackageVersion;
        if (!string.IsNullOrEmpty(provider.EfProviderPackageId))
            config.NuGetPackages[provider.EfProviderPackageId] = provider.EfProviderPackageVersion;

        var cs = config.ConnectionString ?? string.Empty;
        if (string.IsNullOrWhiteSpace(cs) ||
            (!string.IsNullOrEmpty(previous.ConnectionStringTemplate) &&
             string.Equals(cs, previous.ConnectionStringTemplate, StringComparison.Ordinal)))
        {
            if (!string.IsNullOrEmpty(provider.ConnectionStringTemplate))
                config.ConnectionString = provider.ConnectionStringTemplate;
        }
    }
}
