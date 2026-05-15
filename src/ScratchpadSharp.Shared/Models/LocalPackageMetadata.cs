using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;

namespace ScratchpadSharp.Shared.Models;

public class LocalPackageMetadata : IPackageSearchMetadata
{
    public LocalPackageMetadata(PackageIdentity identity)
    {
        Identity = identity;
    }

    public Task<PackageDeprecationMetadata> GetDeprecationMetadataAsync()
    {
        return Task.FromResult<PackageDeprecationMetadata>(null);
    }

    public Task<IEnumerable<VersionInfo>> GetVersionsAsync()
    {
        return Task.FromResult(Enumerable.Empty<VersionInfo>());
    }

    public PackageIdentity Identity { get; }
    public string? Authors { get; set; }
    public string? Description { get; set; }
    public string? Title { get; set; }
    public string? Summary { get; set; }
    public string? Tags { get; set; }
    public string? Owners { get; set; }
    public Uri? IconUrl { get; set; }
    public Uri? LicenseUrl { get; set; }
    public Uri? ProjectUrl { get; set; }
    public Uri? ReadmeUrl { get; set; }
    public Uri? ReportAbuseUrl { get; set; }
    public Uri? PackageDetailsUrl { get; set; }
    public DateTimeOffset? Published { get; set; }
    public bool RequireLicenseAcceptance { get; set; }
    public bool IsListed { get; set; } = true;
    public bool PrefixReserved { get; set; }
    public LicenseMetadata? LicenseMetadata { get; set; }
    public IEnumerable<PackageVulnerabilityMetadata> Vulnerabilities { get; set; } = Enumerable.Empty<PackageVulnerabilityMetadata>();
    public IEnumerable<PackageDependencyGroup> DependencySets { get; set; } = Enumerable.Empty<PackageDependencyGroup>();
    public long? DownloadCount { get; set; }
}