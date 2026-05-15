using NuGet.Common;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Protocol.Core.Types;
using NuGet.Resolver;

namespace ScratchpadSharp.Core.PackageManagement;

public class DependencyResolver
{
    private static readonly System.Lazy<DependencyResolver> LazyInstance = 
        new(() => new DependencyResolver());
    public static DependencyResolver Instance => LazyInstance.Value;
    private DependencyResolver() { }

    public async Task<IEnumerable<PackageIdentity>> ResolveFullGraphAsync(
        IEnumerable<PackageIdentity> rootPackages,
        NuGetFramework framework, 
        CancellationToken ct)
    {
        // 1. 收集所有相关的依赖元数据 (包括递归依赖)
        var availablePackages = new HashSet<SourcePackageDependencyInfo>(PackageIdentityComparer.Default);
        
        // 避免多次枚举
        var roots = rootPackages as PackageIdentity[] ?? rootPackages.ToArray();

        foreach (var root in roots)
        {
            await FetchDependenciesRecursive(root, framework, availablePackages, ct);
        }

        // 如果连根包都找不到元数据，直接返回（或者抛出异常）
        if (!availablePackages.Any())
            return Enumerable.Empty<PackageIdentity>();

        var sourceNames = NuGetService.Instance.PackageSources;

        // 2. 配置 NuGet 解析器上下文
        var resolverContext = new PackageResolverContext(
            DependencyBehavior.Lowest,
            roots.Select(p => p.Id),
            Enumerable.Empty<string>(),
            Enumerable.Empty<PackageReference>(),
            Enumerable.Empty<PackageIdentity>(),
            availablePackages,
            sourceNames, // [Fix] 传入获取到的源名称列表
            NullLogger.Instance);

        // 3. 执行仲裁算法
        var resolver = new PackageResolver();
        try 
        {
            var selectedPackages = resolver.Resolve(resolverContext, ct);
            return selectedPackages;
        }
        catch (NuGet.Resolver.NuGetResolverInputException ex)
        {
            // 这里通常是因为找不到满足条件的包版本
            throw new InvalidOperationException($"依赖解析失败: {ex.Message}", ex);
        }
    }

    private async Task FetchDependenciesRecursive(
        PackageIdentity package,
        NuGetFramework framework,
        HashSet<SourcePackageDependencyInfo> availablePackages,
        CancellationToken ct)
    {
        // 递归终止条件：如果这个包已经抓取过，跳过
        if (availablePackages.Any(p => PackageIdentityComparer.Default.Equals(p, package)))
            return;

        // 调用 NuGetService 获取元数据
        var dependencyInfo = await NuGetService.Instance.GetPackageDependenciesAsync(package, framework, ct);

        // [Fix] 判空保护
        if (dependencyInfo == null) return;

        foreach (var info in dependencyInfo)
        {
            // 添加到池子中供 Resolver 使用
            if (availablePackages.Add(info))
            {
                // 只有当这是新添加的包时，才继续递归它的依赖
                // 这样可以减少一部分重复检查
                foreach (var dependency in info.Dependencies)
                {
                    // [Fix] 保护 MinVersion 为 null 的情况，默认使用 0.0.0
                    var minVersion = dependency.VersionRange?.MinVersion ?? new NuGet.Versioning.NuGetVersion("0.0.0");
                    var depIdentity = new PackageIdentity(dependency.Id, minVersion);
                    
                    await FetchDependenciesRecursive(depIdentity, framework, availablePackages, ct);
                }
            }
        }
    }}