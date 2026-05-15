using System.Collections.Generic;

namespace ScratchpadSharp.Shared.Models;

public class ScriptConfig
{
    public List<string> Usings { get; init; } = [
        "System",
        "System.Linq",
        "System.Collections.Generic",
        "System.Threading.Tasks",
        "System.IO",
        "ScratchpadSharp.Core.External.NetPad.Presentation"
    ];

    public List<string> References { get; init; } =
    [
        "System.Runtime",
        "System.Collections",
        "System.Linq",
        "System.Linq.Expressions",
        "netstandard",
        "System.Private.CoreLib",
        "System.Text.RegularExpressions",
        "System.IO.FileSystem",
        "System.Net.Http"
    ];

    public Dictionary<string, string> NuGetPackages { get; init; } = new();
    public string ConnectionString { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 30;
}
