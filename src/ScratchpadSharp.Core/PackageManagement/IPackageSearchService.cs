using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Frameworks;
using NuGet.Packaging.Core;
using NuGet.Protocol.Core.Types;
using ScratchpadSharp.Shared.Models;

namespace ScratchpadSharp.Core.PackageManagement;

public class ResolvedEnvironment
{
    public List<ResolvedAsset> Assemblies { get; set; } = new();
    public Dictionary<string, List<ResolvedAsset>> NativeAssets { get; set; } = new();
}

public class PackageInstallResult
{
    public bool Success { get; set; }
    public List<string> Assemblies { get; set; } = new();
    public List<PackageIdentity> InstalledPackages { get; set; } = new();
    public string ErrorMessage { get; set; } = string.Empty;
}

public class PackageSearchResult(IPackageSearchMetadata packageSearchMetadata)
{
    public string Id { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? IconUrl { get; set; }
    public string? Authors { get; set; }
    public long? DownloadCount { get; set; }
    public bool IsLocal { get; set; }
    public string? ProjectUrl { get; set; }
    public string? LicenseUrl { get; set; }
    public DateTimeOffset? Published { get; set; }
    public string[] Tags { get; set; } = Array.Empty<string>();
    public IEnumerable<string> Dependencies { get; set; } = Enumerable.Empty<string>();
    public string Source { get; set; } = string.Empty;
}

public record PackageInstallProgress(string Message, double Percent, string PackageId);