using System.Collections.Generic;

namespace ScratchpadSharp.Shared.Models;

public class ScriptConfig
{
    public List<string> DefaultUsings { get; set; }
    public List<string> DefaultReferences { get; set; }
    public Dictionary<string, string> NuGetPackages { get; set; }
    public string ConnectionString { get; set; }
    public int TimeoutSeconds { get; set; }

    public ScriptConfig()
    {
        DefaultUsings = new List<string>
        {
            "System",
            "System.Linq",
            "System.Collections.Generic",
            "System.Threading.Tasks",
            "System.IO"
        };

        DefaultReferences = new List<string>
        {
            "System.Runtime",
            "System.Collections",
            "System.Linq",
            "System.Linq.Expressions",
            "netstandard",
            "System.Private.CoreLib",
            "System.Text.RegularExpressions",
            "System.IO.FileSystem",
            "System.Net.Http"
        };

        NuGetPackages = new Dictionary<string, string>();
        ConnectionString = string.Empty;
        TimeoutSeconds = 30;
    }
}
