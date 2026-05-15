using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ScratchpadSharp.Shared.Models;

public class PackageManifest
{
    public string FormatVersion { get; set; } = "1.0";
    public DateTime Created { get; set; }
    public DateTime Modified { get; set; }
    public MetadataInfo Metadata { get; set; } = new();
    public ProjectMetadata Project { get; set; } = new();
    public CompilationContext Compilation { get; set; } = new();
    public ResolvedState ResolvedState { get; set; } = new();
    [JsonIgnore]
    public bool IsProvided { get; set; } = false;
}

public class MetadataInfo
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = new();
}

public class ProjectMetadata
{
    public string EntryPoint { get; set; } = "code.cs";
    public string TargetFramework { get; set; } = "net8.0";
}

public class CompilationContext
{
    public string Nullable { get; set; } = "enable";
    public bool AllowUnsafe { get; set; } = false;
    public List<string> ImplicitUsings { get; set; } = new();
}

public class ResolvedState
{
    public List<ResolvedAsset> Assemblies { get; set; } = new();
    // RID as key
    public Dictionary<string, List<ResolvedAsset>> NativeAssets { get; set; } = new();
}

public class ResolvedAsset
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AssetOrigin Origin { get; set; }

    public string Id { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Version { get; set; }

    public string RelativePath { get; set; } = string.Empty;
}

public enum AssetOrigin
{
    NuGet,
    Local
}
