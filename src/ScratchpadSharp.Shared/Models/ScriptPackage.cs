using System;

namespace ScratchpadSharp.Shared.Models;

public class ScriptPackage
{
    public string Code { get; set; } = string.Empty;
    public ScriptConfig Config { get; set; } = new();
    public PackageManifest Manifest { get; set; } = new();
    public string Output { get; set; } = string.Empty;
}
