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
        new(DatabaseProviderIds.Sqlite, "SQLite",
            "Microsoft.EntityFrameworkCore.Sqlite", EfCorePackageVersion,
            "UseSqlite", "Data Source=scratchpad.db"),
        new(DatabaseProviderIds.SqlServer, "SQL Server",
            "Microsoft.EntityFrameworkCore.SqlServer", EfCorePackageVersion,
            "UseSqlServer",
            "Server=localhost;Database=Scratchpad;Trusted_Connection=True;TrustServerCertificate=True")
    ];

    public static IReadOnlyList<DatabaseProviderInfo> SelectableProviders { get; } = All;

    public static DatabaseProviderInfo Get(string? id)
    {
        var normalized = string.IsNullOrWhiteSpace(id) ? DatabaseProviderIds.Sqlite : id.Trim();
        return All.FirstOrDefault(p =>
                   p.Id.Equals(normalized, StringComparison.OrdinalIgnoreCase))
               ?? All[0];
    }

    /// <summary>Sets EF NuGet packages on a module instance config.</summary>
    public static void ApplyModulePackages(ModuleInstanceConfig config)
    {
        var provider = Get(config.ProviderId);
        config.ProviderId = provider.Id;

        foreach (var id in ProviderPackageIds)
            config.NuGetPackages.Remove(id);

        config.NuGetPackages[EfCorePackageId] = EfCorePackageVersion;
        if (!string.IsNullOrEmpty(provider.EfProviderPackageId))
            config.NuGetPackages[provider.EfProviderPackageId] = provider.EfProviderPackageVersion;
    }
}
