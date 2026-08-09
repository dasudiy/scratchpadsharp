using System.Collections.Generic;

namespace ScratchpadSharp.Shared.Models;

public sealed class ModuleSourceFile
{
    public string FileName { get; set; } = string.Empty;
    public string SourceText { get; set; } = string.Empty;
}

/// <summary>Effective compile inputs after merging query config with referenced module instances.</summary>
public sealed class MergedScriptEnvironment
{
    public List<string> Usings { get; set; } = [];
    public List<string> References { get; set; } = [];
    public Dictionary<string, string> NuGetPackages { get; set; } = new();
    public List<ModuleSourceFile> ModuleSources { get; set; } = [];
    public List<ModuleInstanceConfig> ResolvedModules { get; set; } = [];
}
