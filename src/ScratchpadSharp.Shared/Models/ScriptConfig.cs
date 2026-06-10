using System.Collections.Generic;

namespace ScratchpadSharp.Shared.Models;

public class ScriptConfig
{
    public List<string> Usings { get; set; } = [];
    public List<string> References { get; set; } = [];
    public Dictionary<string, string> NuGetPackages { get; set; } = new();
    public string ConnectionString { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; }

    public ScriptConfig Clone() => new()
    {
        Usings = [..Usings],
        References = [..References],
        NuGetPackages = new Dictionary<string, string>(NuGetPackages),
        ConnectionString = ConnectionString,
        TimeoutSeconds = TimeoutSeconds
    };
}
