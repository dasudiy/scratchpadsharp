using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using ScratchpadSharp.Shared.Models;

namespace ScratchpadSharp.Core.Database;

public sealed class SqlServerSchemaProvider : IDbSchemaProvider
{
    public string ProviderId => DatabaseProviderIds.SqlServer;

    public async Task<ConnectionTestResult> TestConnectionAsync(string connectionString,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            return new ConnectionTestResult(false, "Connection string is empty.");

        var sw = Stopwatch.StartNew();
        try
        {
            await using var connection = new SqlConnection(connectionString);
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
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);

        var tables = new List<DbTableInfo>();
        var tableKeys = new List<(string Schema, string Name, bool IsView)>();

        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText =
                """
                SELECT TABLE_SCHEMA, TABLE_NAME, TABLE_TYPE
                FROM INFORMATION_SCHEMA.TABLES
                WHERE TABLE_TYPE IN ('BASE TABLE', 'VIEW')
                ORDER BY TABLE_SCHEMA, TABLE_NAME;
                """;

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var schema = reader.GetString(0);
                var name = reader.GetString(1);
                var isView = string.Equals(reader.GetString(2), "VIEW", StringComparison.OrdinalIgnoreCase);
                tableKeys.Add((schema, name, isView));
            }
        }

        var pkSet = await LoadPrimaryKeysAsync(connection, ct);

        foreach (var (schema, name, isView) in tableKeys)
        {
            var columns = await LoadColumnsAsync(connection, schema, name, pkSet, ct);
            tables.Add(new DbTableInfo(name, schema, isView, columns));
        }

        return new DbSchemaSnapshot(tables, connection.Database);
    }

    private static async Task<HashSet<string>> LoadPrimaryKeysAsync(SqlConnection connection,
        CancellationToken ct)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText =
            """
            SELECT ku.TABLE_SCHEMA, ku.TABLE_NAME, ku.COLUMN_NAME
            FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS AS tc
            INNER JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE AS ku
              ON tc.CONSTRAINT_TYPE = 'PRIMARY KEY'
             AND tc.CONSTRAINT_NAME = ku.CONSTRAINT_NAME
             AND tc.TABLE_SCHEMA = ku.TABLE_SCHEMA
             AND tc.TABLE_NAME = ku.TABLE_NAME;
            """;

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            keys.Add($"{reader.GetString(0)}.{reader.GetString(1)}.{reader.GetString(2)}");

        return keys;
    }

    private static async Task<IReadOnlyList<DbColumnInfo>> LoadColumnsAsync(SqlConnection connection,
        string schema, string table, HashSet<string> pkSet, CancellationToken ct)
    {
        var columns = new List<DbColumnInfo>();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText =
            """
            SELECT COLUMN_NAME, DATA_TYPE, IS_NULLABLE, ORDINAL_POSITION
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = @schema AND TABLE_NAME = @table
            ORDER BY ORDINAL_POSITION;
            """;
        cmd.Parameters.AddWithValue("@schema", schema);
        cmd.Parameters.AddWithValue("@table", table);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var name = reader.GetString(0);
            var type = reader.GetString(1);
            var nullable = string.Equals(reader.GetString(2), "YES", StringComparison.OrdinalIgnoreCase);
            var ordinal = Convert.ToInt32(reader.GetValue(3));
            var isPk = pkSet.Contains($"{schema}.{table}.{name}");
            columns.Add(new DbColumnInfo(name, type, nullable, isPk, ordinal));
        }

        return columns;
    }
}
