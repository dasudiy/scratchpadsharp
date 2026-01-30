using System.Collections.Generic;

namespace ScratchpadSharp.Shared.Models;

public class ScriptConfig
{
    public List<string> DefaultUsings { get; set; } = new();
    public Dictionary<string, string> NuGetPackages { get; set; } = new();
    public string ConnectionString { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 30;
}
