using System.Collections.Generic;
using System.Threading.Tasks;
using NuGet.Frameworks;
using NuGet.Packaging.Core;
using NuGet.Protocol.Core.Types;

namespace ScratchpadSharp.Core.PackageManagement;

public interface INuGetService
{
    Task<IEnumerable<IPackageSearchMetadata>> SearchAsync(string term, bool includePreRelease, string? sourceName = null,
        CancellationToken cancellationToken = default);

    Task<IEnumerable<IPackageSearchMetadata>> GetLocalPackagesAsync(CancellationToken cancellationToken = default);
    Task<IEnumerable<string>> GetPackageVersionsAsync(string packageId, CancellationToken cancellationToken = default);

    Task<PackageInstallResult> EnsurePackageDownloadedAsync(string packageId, string version,
        IProgress<PackageInstallProgress> progress, CancellationToken cancellationToken = default);

    Task<IEnumerable<string>> GetPackageSourcesAsync(CancellationToken cancellationToken = default);

    Task<IEnumerable<SourcePackageDependencyInfo>> GetPackageDependenciesAsync(PackageIdentity package,
        NuGetFramework framework);

    // Task<ResolvedEnvironment> ResolveEnvironmentAsync(IEnumerable<PackageIdentity> packages, CancellationToken cancellationToken = default);
}