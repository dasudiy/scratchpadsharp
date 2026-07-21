using System.Collections.Generic;

namespace ScratchpadSharp.Core.Database;

public sealed record DbQueryResult(
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyList<string?>> Rows,
    int? RecordsAffected = null);
