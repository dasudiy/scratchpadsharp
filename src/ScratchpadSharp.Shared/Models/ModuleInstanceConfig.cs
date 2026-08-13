using System.Collections.Generic;

namespace ScratchpadSharp.Shared.Models;

public class ModuleInstanceConfig
{
    public string Id { get; set; } = string.Empty;
    public string TypeId { get; set; } = ModuleTypeIds.EfCore;
    public string DisplayName { get; set; } = string.Empty;
    /// <summary>C# namespace segment under Modules.* (e.g. LocalSqlite).</summary>
    public string NamespaceSegment { get; set; } = string.Empty;
    public string ProviderId { get; set; } = string.Empty;
    public string ConnectionString { get; set; } = string.Empty;
    /// <summary>OS-user encrypted SQL password. The connection string is stored without Password=.</summary>
    public string? EncryptedDatabasePassword { get; set; }
    /// <summary>Optional SSH tunnel. Null or <see cref="SshTunnelConfig.Enabled"/> false means a direct connection.</summary>
    public SshTunnelConfig? SshTunnel { get; set; }
    public List<string> Usings { get; set; } = [];
    public Dictionary<string, string> NuGetPackages { get; set; } = new();

    public string FullNamespace => $"Modules.{NamespaceSegment}";
}
