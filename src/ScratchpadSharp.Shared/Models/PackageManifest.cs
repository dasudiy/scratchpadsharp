using System;
using System.Collections.Generic;

namespace ScratchpadSharp.Shared.Models;

public class PackageManifest
{
    public string FormatVersion { get; set; } = "1.0";
    public DateTime Created { get; set; }
    public DateTime Modified { get; set; }
    public MetadataInfo Metadata { get; set; } = new();
}

public class MetadataInfo
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = new();
}
