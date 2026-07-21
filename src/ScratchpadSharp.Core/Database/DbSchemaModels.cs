using System.Collections.Generic;

namespace ScratchpadSharp.Core.Database;

public sealed record ConnectionTestResult(
    bool Success,
    string Message,
    string? ServerVersion = null,
    long ElapsedMilliseconds = 0);

public sealed record DbColumnInfo(
    string Name,
    string DataType,
    bool IsNullable,
    bool IsPrimaryKey,
    int Ordinal);

public sealed record DbTableInfo(
    string Name,
    string Schema,
    bool IsView,
    IReadOnlyList<DbColumnInfo> Columns);

public sealed record DbSchemaSnapshot(
    IReadOnlyList<DbTableInfo> Tables,
    string? DatabaseName = null);
