using System.Threading;
using System.Threading.Tasks;

namespace ScratchpadSharp.Core.Database;

public interface IDbSchemaProvider
{
    string ProviderId { get; }

    Task<ConnectionTestResult> TestConnectionAsync(string connectionString, CancellationToken ct = default);

    Task<DbSchemaSnapshot> GetSchemaAsync(string connectionString, CancellationToken ct = default);
}
