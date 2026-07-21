using System;
using ScratchpadSharp.Shared.Models;

namespace ScratchpadSharp.Core.Database;

public static class DbSchemaProviderFactory
{
    public static IDbSchemaProvider? Create(string? providerId)
    {
        var id = DatabaseProviderCatalog.Get(providerId).Id;
        return id switch
        {
            DatabaseProviderIds.Sqlite => new SqliteSchemaProvider(),
            DatabaseProviderIds.SqlServer => new SqlServerSchemaProvider(),
            _ => null
        };
    }

    public static bool SupportsSchema(string? providerId) => Create(providerId) != null;
}
