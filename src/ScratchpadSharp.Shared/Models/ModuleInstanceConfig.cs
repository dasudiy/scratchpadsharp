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
    public List<string> Usings { get; set; } = [];
    public Dictionary<string, string> NuGetPackages { get; set; } = new();

    public string FullNamespace => $"Modules.{NamespaceSegment}";
}
