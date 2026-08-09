using System;
using System.Linq;
using ScratchpadSharp.Shared.Models;

namespace ScratchpadSharp.Core.Database;

public static class DbSchemaProviderFactory
{
    public static IDbSchemaProvider Create(string? providerId)
    {
        var id = DatabaseProviderCatalog.Get(providerId).Id;
        return id switch
        {
            DatabaseProviderIds.Sqlite => new SqliteSchemaProvider(),
            DatabaseProviderIds.SqlServer => new SqlServerSchemaProvider(),
            _ => throw new ArgumentException($"Unsupported database provider: {providerId}", nameof(providerId))
        };
    }

    public static bool SupportsSchema(string? providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId))
            return false;

        var normalized = providerId.Trim();
        return DatabaseProviderCatalog.All.Any(p =>
            p.Id.Equals(normalized, StringComparison.OrdinalIgnoreCase));
    }
}
