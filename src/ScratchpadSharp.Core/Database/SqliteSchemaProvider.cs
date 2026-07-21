using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using ScratchpadSharp.Shared.Models;

namespace ScratchpadSharp.Core.Database;

public sealed class SqliteSchemaProvider : IDbSchemaProvider
{
    public string ProviderId => DatabaseProviderIds.Sqlite;

    public async Task<ConnectionTestResult> TestConnectionAsync(string connectionString,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            return new ConnectionTestResult(false, "Connection string is empty.");

        var sw = Stopwatch.StartNew();
        try
        {
            await using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync(ct);
            sw.Stop();
            return new ConnectionTestResult(true, "Connected.", connection.ServerVersion, sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new ConnectionTestResult(false, ex.Message, ElapsedMilliseconds: sw.ElapsedMilliseconds);
        }
    }

    public async Task<DbSchemaSnapshot> GetSchemaAsync(string connectionString, CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(ct);

        var tables = new List<DbTableInfo>();
        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText =
                """
                SELECT name, type FROM sqlite_master
                WHERE type IN ('table', 'view')
                  AND name NOT LIKE 'sqlite_%'
                ORDER BY type, name;
                """;

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var name = reader.GetString(0);
                var isView = string.Equals(reader.GetString(1), "view", StringComparison.OrdinalIgnoreCase);
                var columns = await LoadColumnsAsync(connection, name, ct);
                tables.Add(new DbTableInfo(name, "main", isView, columns));
            }
        }

        return new DbSchemaSnapshot(tables, connection.DataSource);
    }

    private static async Task<IReadOnlyList<DbColumnInfo>> LoadColumnsAsync(SqliteConnection connection,
        string tableName, CancellationToken ct)
    {
        var columns = new List<DbColumnInfo>();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({QuoteIdent(tableName)});";

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            // cid, name, type, notnull, dflt_value, pk
            var ordinal = reader.GetInt32(0);
            var name = reader.GetString(1);
            var type = reader.IsDBNull(2) ? "" : reader.GetString(2);
            var notNull = reader.GetInt32(3) != 0;
            var isPk = reader.GetInt32(5) != 0;
            columns.Add(new DbColumnInfo(name, type, !notNull, isPk, ordinal));
        }

        return columns;
    }

    private static string QuoteIdent(string name) =>
        "\"" + name.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}
