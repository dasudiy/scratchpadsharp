using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using ScratchpadSharp.Shared.Models;

namespace ScratchpadSharp.Core.PackageManagement;

public class NuGetService
{
    private static readonly Lazy<NuGetService> LazyInstance = new(() => new NuGetService());
    public static NuGetService Instance => LazyInstance.Value;

    private readonly string globalPackagesFolder;
    private readonly List<PackageSource> packageSources;
    public IEnumerable<PackageSource> PackageSources => packageSources;

    private NuGetService()
    {
        var settings = Settings.LoadDefaultSettings(null);
        globalPackagesFolder = SettingsUtility.GetGlobalPackagesFolder(settings);

        var packageSourceProvider = new PackageSourceProvider(settings);
        packageSources = packageSourceProvider.LoadPackageSources()
            .Where(s => s.IsEnabled)
            .ToList();

        if (!packageSources.Any())
        {
            // Fallback to NuGet.org if no sources configured
            packageSources.Add(new PackageSource("https://api.nuget.org/v3/index.json", "nuget.org"));
        }
    }

    public async Task<IEnumerable<string>> GetPackageSourcesAsync(CancellationToken cancellationToken = default)
    {
        return await Task.FromResult(packageSources.Select(s => s.Name).ToList());
    }

    public async Task<IEnumerable<SourcePackageDependencyInfo>> GetPackageDependenciesAsync(PackageIdentity package,
        NuGetFramework framework, CancellationToken cancellationToken)
    {
        using var cache = new SourceCacheContext();
        
        foreach (var repo in packageSources.Select(t=> Repository.Factory.GetCoreV3(t)))
        {
            try 
            {
                var dependencyResource = await repo.GetResourceAsync<DependencyInfoResource>(cancellationToken);
                // ResolvePackage 获取单个包的依赖信息
                var info = await dependencyResource.ResolvePackage(package, framework, cache, NullLogger.Instance, cancellationToken);
                
                if (info != null)
                {
                    // 虽然方法签名返回 IEnumerable，但通常 ResolvePackage 返回单个节点的详细信息
                    // 我们将其包装成列表返回，因为 DependencyResolver 可能期望处理集合
                    return [info];
                }
            }
            catch
            {
                // 忽略单个源的错误，尝试下一个源
            }
        }
        
        return Enumerable.Empty<SourcePackageDependencyInfo>();
    }

    public async Task<IEnumerable<IPackageSearchMetadata>> SearchAsync(string term, bool includePreRelease,
        string? sourceName = null, CancellationToken cancellationToken = default)
    {
        var results = new ConcurrentBag<IPackageSearchMetadata>();
        var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = cancellationToken };

        var sourcesToSearch = packageSources;
        if (!string.IsNullOrEmpty(sourceName))
        {
            sourcesToSearch = packageSources.Where(s => s.Name.Equals(sourceName, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        await Parallel.ForEachAsync(sourcesToSearch, parallelOptions, async (source, token) =>
        {
            try
            {
                var repository = Repository.Factory.GetCoreV3(source);
                var resource = await repository.GetResourceAsync<PackageSearchResource>(token);
                if (resource == null) return;

                var searchFilter = new SearchFilter(includePreRelease);
                foreach (var item in await resource.SearchAsync(term, searchFilter, 0, 20, NullLogger.Instance, token))
                {
                    results.Add(item);
                }
            }
            catch (Exception)
            {
                // Ignore errors from individual sources
            }
        });

        return results.OrderByDescending(r => r.DownloadCount);
    }

    public string GetCachedPackagePath(PackageIdentity package) =>
        Path.Combine(
            globalPackagesFolder,
            package.Id.ToLowerInvariant(),
            package.Version.ToNormalizedString().ToLowerInvariant());

    public bool IsPackageCached(PackageIdentity package) =>
        Directory.Exists(GetCachedPackagePath(package));

    public Task<IEnumerable<string>> GetCachedVersionsAsync(string packageId,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var packageDir = Path.Combine(globalPackagesFolder, packageId.ToLowerInvariant());
            if (!Directory.Exists(packageDir))
                return Enumerable.Empty<string>();

            return Directory.GetDirectories(packageDir)
                .Select(Path.GetFileName)
                .Where(v => !string.IsNullOrEmpty(v) && NuGetVersion.TryParse(v, out _))
                .Select(v => NuGetVersion.Parse(v!).ToNormalizedString())
                .Distinct()
                .OrderByDescending(v => NuGetVersion.Parse(v))
                .ToList()
                .AsEnumerable();
        }, cancellationToken);
    }

    public async Task<IPackageSearchMetadata?> GetPackageMetadataAsync(PackageIdentity identity,
        CancellationToken cancellationToken = default)
    {
        var cachedPath = GetCachedPackagePath(identity);
        if (Directory.Exists(cachedPath))
            return await Task.FromResult(CreateMetadataFromFolder(identity, cachedPath));

        using var cache = new SourceCacheContext();
        foreach (var source in packageSources)
        {
            try
            {
                var repository = Repository.Factory.GetCoreV3(source);
                var resource = await repository.GetResourceAsync<PackageMetadataResource>(cancellationToken);
                var metadata = await resource.GetMetadataAsync(identity, cache, NullLogger.Instance, cancellationToken);
                if (metadata != null)
                    return metadata;
            }
            catch
            {
                // Try next source
            }
        }

        return null;
    }

    public async Task<IEnumerable<IPackageSearchMetadata>> GetLocalPackagesAsync(
        CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            if (!Directory.Exists(globalPackagesFolder))
                return Enumerable.Empty<IPackageSearchMetadata>();

            var packages = LocalFolderUtility.GetPackagesV3(globalPackagesFolder, NullLogger.Instance);

            return packages
                .GroupBy(p => p.Nuspec.GetId(), StringComparer.OrdinalIgnoreCase)
                .Select(group =>
                {
                    var latest = group.OrderByDescending(p => p.Identity.Version).First();
                    var properId = latest.Nuspec.GetId();
                    var identity = new PackageIdentity(properId, latest.Identity.Version);

                    var latestPath = latest.Path ?? GetCachedPackagePath(identity);

                    Uri? ParseUri(string? url) =>
                        !string.IsNullOrEmpty(url) && Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri : null;

                    return (IPackageSearchMetadata)new LocalPackageMetadata(identity)
                    {
                        Description = latest.Nuspec.GetDescription(),
                        Authors = latest.Nuspec.GetAuthors(),
                        Title = latest.Nuspec.GetTitle(),
                        Summary = latest.Nuspec.GetSummary(),
                        Owners = latest.Nuspec.GetOwners(),
                        Tags = latest.Nuspec.GetTags(),
                        IconUrl = ParseUri(latest.Nuspec.GetIconUrl()),
                        LicenseUrl = ParseUri(latest.Nuspec.GetLicenseUrl()),
                        ProjectUrl = ParseUri(latest.Nuspec.GetProjectUrl()),
                        RequireLicenseAcceptance = latest.Nuspec.GetRequireLicenseAcceptance(),
                        DependencySets = latest.Nuspec.GetDependencyGroups().ToList(),
                        Published = Directory.Exists(latestPath)
                            ? new DirectoryInfo(latestPath).CreationTimeUtc
                            : DateTimeOffset.UtcNow
                    };
                })
                .OrderBy(m => m.Identity.Id, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }, cancellationToken);
    }

    private static LocalPackageMetadata CreateMetadataFromFolder(PackageIdentity identity, string packagePath)
    {
        var nuspecPath = Directory.GetFiles(packagePath, "*.nuspec").FirstOrDefault()
                         ?? throw new FileNotFoundException("No .nuspec in package folder", packagePath);

        using var stream = File.OpenRead(nuspecPath);
        var nuspec = new NuspecReader(stream);
        var properId = nuspec.GetId();

        Uri? ParseUri(string? url) =>
            !string.IsNullOrEmpty(url) && Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri : null;

        return new LocalPackageMetadata(new PackageIdentity(properId, identity.Version))
        {
            Description = nuspec.GetDescription(),
            Authors = nuspec.GetAuthors(),
            Title = nuspec.GetTitle(),
            Summary = nuspec.GetSummary(),
            Owners = nuspec.GetOwners(),
            Tags = nuspec.GetTags(),
            IconUrl = ParseUri(nuspec.GetIconUrl()),
            LicenseUrl = ParseUri(nuspec.GetLicenseUrl()),
            ProjectUrl = ParseUri(nuspec.GetProjectUrl()),
            RequireLicenseAcceptance = nuspec.GetRequireLicenseAcceptance(),
            DependencySets = nuspec.GetDependencyGroups().ToList(),
            Published = Directory.Exists(packagePath)
                ? new DirectoryInfo(packagePath).CreationTimeUtc
                : DateTimeOffset.UtcNow
        };
    }

    public async Task<IEnumerable<string>> GetPackageVersionsAsync(string packageId,
        CancellationToken cancellationToken = default)
    {
        var allVersions = new ConcurrentBag<NuGetVersion>();
        var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = cancellationToken };

        await Parallel.ForEachAsync(packageSources, parallelOptions, async (source, token) =>
        {
            using (var cacheContext = new SourceCacheContext())
            {
                try
                {
                    var repository = Repository.Factory.GetCoreV3(source);
                    var resource = await repository.GetResourceAsync<FindPackageByIdResource>(token);
                    if (resource == null) return;

                    var versions =
                        await resource.GetAllVersionsAsync(packageId, cacheContext, NullLogger.Instance, token);
                    foreach (var v in versions) allVersions.Add(v);
                }
                catch
                {
                    // ignored
                }
            }
        });

        return allVersions.OrderByDescending(v => v).Select(v => v.ToString()).Distinct();
    }
    
    
    /// <summary>
    /// 确保指定的包已下载并解压到全局缓存中。
    /// 此步骤不需要 targetFramework，因为它下载的是包含所有框架的完整包。
    /// </summary>
    public async Task<string> EnsurePackageDownloadedAsync(
        PackageIdentity package,
        CancellationToken ct,
        IProgress<PackageInstallProgress>? progress = null)
    {
        var expectedPath = GetCachedPackagePath(package);

        // 1. 快速路径：目录已存在则直接返回，不触发下载
        if (Directory.Exists(expectedPath))
        {
            progress?.Report(new PackageInstallProgress(
                $"Using cached {package.Id} {package.Version}", 100, package.Id));
            return expectedPath;
        }

        // 2. 慢速路径：从网络下载
        progress?.Report(new PackageInstallProgress(
            $"Downloading {package.Id} {package.Version}...", 0, package.Id));

        using var cacheContext = new SourceCacheContext();
        var repositories = packageSources.Select(t => Repository.Factory.GetCoreV3(t));
        var failures = new List<string>();

        foreach (var repo in repositories)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var downloadResource = await repo.GetResourceAsync<DownloadResource>(ct);

                var downloadResult = await downloadResource.GetDownloadResourceResultAsync(
                    package,
                    new PackageDownloadContext(cacheContext),
                    globalPackagesFolder,
                    NullLogger.Instance,
                    ct);

                if (downloadResult.Status == DownloadResourceResultStatus.Available ||
                    downloadResult.Status == DownloadResourceResultStatus.AvailableWithoutStream)
                {
                    expectedPath = GetCachedPackagePath(package);
                    if (Directory.Exists(expectedPath))
                    {
                        progress?.Report(new PackageInstallProgress(
                            $"Downloaded {package.Id} {package.Version}", 100, package.Id));
                        return expectedPath;
                    }
                }
                else
                {
                    failures.Add($"Source {repo.PackageSource.Name}: Status {downloadResult.Status}");
                }
            }
            catch (Exception ex)
            {
                failures.Add($"Source {repo.PackageSource.Name}: {ex.Message}");
            }
        }

        throw new InvalidOperationException(
            $"Unable to find package '{package.Id} {package.Version}'. Errors: {string.Join("; ", failures)}");
    }
    
    /// <summary>
    /// [新增] 从已下载的包目录中，提取适合指定框架 (e.g. net8.0) 的 DLL 和 Native 资源
    /// </summary>
    public Task<PackageAssets> GetPackageAssetsAsync(string packageRootPath, NuGetFramework targetFramework)
    {
        return Task.Run(() => 
        {
            var compileRefs = new List<string>();
            var nativePaths = new Dictionary<string, List<string>>();

            using var packageReader = new PackageFolderReader(packageRootPath);
            var frameworkReducer = new FrameworkReducer();

            // --- 1. 提取编译引用 (Compile References) ---
            // 优先查找 ref 文件夹 (Metadata Only, 编译速度快)，如果没有则降级到 lib
            var referenceItems = packageReader.GetReferenceItems().ToList();
            if (!referenceItems.Any())
            {
                referenceItems = packageReader.GetLibItems().ToList();
            }

            var nearestRef = frameworkReducer.GetNearest(targetFramework, referenceItems.Select(x => x.TargetFramework));
            
            if (nearestRef != null)
            {
                var group = referenceItems.First(x => x.TargetFramework.Equals(nearestRef));
                foreach (var item in group.Items)
                {
                    if (IsValidDll(item))
                    {
                        compileRefs.Add(Path.Combine(packageRootPath, item));
                    }
                }
            }

            // --- 2. 提取 Native 资源 (Runtime Native Assets) ---
            // 扫描 runtimes 文件夹。这里简单提取所有 runtimes，或者可以根据当前 RID 过滤
            // 为了生成跨平台的 manifest，我们这里可以提取所有，或者由调用方决定
            // 这里演示提取当前运行环境的 Native 库 (更实用)
            
            var runtimeItems = packageReader.GetItems("runtimes").ToList(); // 获取 runtimes 下的所有组
        
            foreach (var rid in runtimeItems)
            {
                nativePaths[rid.TargetFramework.ToString()] = new List<string>();
                // 通常 native 库在 runtimes/{rid}/native/ 下
                foreach (var item in rid.Items)
                {
                    if (item.Contains("/native/") && (item.EndsWith(".dll") || item.EndsWith(".so") || item.EndsWith(".dylib")))
                    {
                        nativePaths[rid.TargetFramework.ToString()].Add(Path.Combine(packageRootPath, item));
                    }
                }
            }
            
            // 如果包根目录直接有 native (较少见，但也可能)
            // ...

            return new PackageAssets(compileRefs, nativePaths);
        });
    }

    private bool IsValidDll(string path)
    {
        return Path.GetExtension(path).Equals(".dll", StringComparison.OrdinalIgnoreCase) &&
               !path.EndsWith(".resources.dll", StringComparison.OrdinalIgnoreCase);
    }


}