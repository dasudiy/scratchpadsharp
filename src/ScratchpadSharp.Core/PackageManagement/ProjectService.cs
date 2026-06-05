using System.Runtime.InteropServices;
using NuGet.Configuration;
using NuGet.Frameworks;
using NuGet.Packaging.Core;
using NuGet.Versioning;
using ScratchpadSharp.Core.Services;
using ScratchpadSharp.Core.Storage;
using ScratchpadSharp.Shared.Models;

namespace ScratchpadSharp.Core.PackageManagement;

public class ProjectService
{
    private static readonly Lazy<ProjectService> LazyInstance = new(() => new ProjectService());
    public static ProjectService Instance => LazyInstance.Value;

    private readonly string globalPackagesFolder;

    private ProjectService()
    {
        var settings = Settings.LoadDefaultSettings(null);
        globalPackagesFolder = SettingsUtility.GetGlobalPackagesFolder(settings);
    }

    /// <summary>
    /// 打开/加载项目。
    /// 自动处理解压、依赖检查、路径补水和 Roslyn 初始化。
    /// </summary>
    public async Task<ProjectContext> NewProjectAsync(string tabId, CancellationToken ct = default)
    {
        var packageDto = new ScriptPackage();

        // use temp path
        var path = Path.GetTempFileName();
        File.Delete(path);
        Directory.CreateDirectory(path);

        // 记录日志：Manifest 缺失，正在重建依赖图...
        await ResolveAndSaveAsync(packageDto, path, ct);

        // 4. 补水 (Hydration): 将相对路径转为绝对路径
        var context = new ProjectContext
        {
            SourcePath = null,
            EffectiveRootPath = path,
            Manifest = packageDto.Manifest!,
            Config = packageDto.Config
        };

        HydratePaths(context);

        // 5. 激活环境 (Roslyn)
        RoslynWorkspaceService.Instance.RemoveProject(tabId);
        RoslynWorkspaceService.Instance.CreateProject(tabId);
        await RoslynWorkspaceService.Instance.UpdateReferencesAsync(tabId, context.AbsoluteCompileReferences);
        // TODO: 如果需要，在这里注入 Compiler Options (AllowUnsafe, Nullable 等)

        return context;
    }

    /// <summary>
    /// 打开/加载项目。
    /// 自动处理解压、依赖检查、路径补水和 Roslyn 初始化。
    /// </summary>
    public async Task<ProjectContext> LoadProjectAsync(string tabId, string path, CancellationToken ct = default)
    {
        // 1. 物理读取 (Data Transfer Object)
        var packageDto = await PackageService.Instance.LoadAsync(path);

        // 2. 确定运行时根目录
        // 如果是 Zip 包，PackageService 内部可能已经解压到了 Temp，或者我们需要在这里处理
        // 假设 LoadAsync 返回的 packageDto.RootPath 已经是指向可访问的物理路径（Tmp 或 Folder）
        string effectiveRoot = packageDto.RootPath;

        // 3. 状态检查与自动修复 (Self-Healing)
        // 如果 Manifest 为空，或者与 Config 不匹配（这里简单判断是否有 Manifest），则触发 Resolve
        if (!packageDto.Manifest.IsProvided || !packageDto.Manifest.ResolvedState.Assemblies.Any())
        {
            // 记录日志：Manifest 缺失，正在重建依赖图...
            await ResolveAndSaveAsync(packageDto, path, ct);
        }

        // 4. 补水 (Hydration): 将相对路径转为绝对路径
        var context = new ProjectContext
        {
            SourcePath = path,
            EffectiveRootPath = effectiveRoot,
            Manifest = packageDto.Manifest!,
            Config = packageDto.Config,
            Code = packageDto.Code
        };

        HydratePaths(context);

        // 5. 激活环境 (Roslyn)
        RoslynWorkspaceService.Instance.RemoveProject(tabId);
        RoslynWorkspaceService.Instance.CreateProject(tabId);
        await RoslynWorkspaceService.Instance.UpdateReferencesAsync(tabId, context.AbsoluteCompileReferences);
        // TODO: 如果需要，在这里注入 Compiler Options (AllowUnsafe, Nullable 等)


        return context;
    }

    public async Task SaveProjectAsync(ProjectContext projectContext)
    {
        var packageDto = new ScriptPackage
        {
            Code = projectContext.Code,
            Config = projectContext.Config,
            Manifest = projectContext.Manifest,
            Output = projectContext.Output,
            RootPath = projectContext.EffectiveRootPath,
        };
        await PackageService.Instance.SaveAsync(packageDto, projectContext.SourcePath ?? throw new
            InvalidOperationException("Source path is null"));
    }

    /// <summary>
    /// 添加 NuGet 包引用。
    /// 更新 Config -> 重新 Resolve -> 更新 Manifest -> 刷新环境。
    /// </summary>
    public async Task AddPackageAsync(string tabId, ProjectContext context, PackageIdentity package,
        CancellationToken ct = default)
    {
        // 1. 更新意图 (Config)
        context.Config.NuGetPackages[package.Id] = package.Version.ToString();

        // 2. 重新计算并保存 (Logic + IO)
        // 为了复用逻辑，这里我们需要重新构建一个 DTO 或者直接操作 Manifest
        // 简单起见，我们假设有一个方法能把 Context 转回 DTO，或者直接在 Context 上操作
        var packageDto = new ScriptPackage
        {
            Config = context.Config,
            Manifest = context.Manifest,
            RootPath = context.EffectiveRootPath
        };

        await ResolveAndSaveAsync(packageDto, context.SourcePath ?? context.EffectiveRootPath, ct);

        // 3. 重新补水并刷新
        HydratePaths(context);
        await RoslynWorkspaceService.Instance.UpdateReferencesAsync(tabId, context.AbsoluteCompileReferences);
    }

    /// <summary>
    /// [新增] 添加本地程序集引用
    /// </summary>
    public async Task AddReferenceAsync(string tabId, ProjectContext context, string referencePath,
        CancellationToken ct = default)
    {
        if (!File.Exists(referencePath))
            throw new FileNotFoundException("Reference file not found", referencePath);

        // 1. 尝试计算相对路径 (用于存入 Config)
        // 只有当文件在项目目录下时，存相对路径才有意义；否则存绝对路径
        // 或者我们可以强制要求用户把 DLL 拷到项目里？这里假设支持任意路径
        string configPath = referencePath;
        if (!string.IsNullOrEmpty(context.EffectiveRootPath))
        {
            try
            {
                configPath = Path.GetRelativePath(context.EffectiveRootPath, referencePath);
            }
            catch
            {
                /* 跨盘符无法计算相对路径，保持绝对路径 */
            }
        }

        // 2. 更新 Config
        if (!context.Config.References.Contains(configPath))
        {
            context.Config.References.Add(configPath);
        }

        // 3. 更新 Manifest (手动脱水)
        // 本地引用不需要 ResolveGraph，直接添加即可
        var localAsset = new ResolvedAsset
        {
            Id = Path.GetFileName(referencePath),
            Origin = AssetOrigin.Local,
            RelativePath = configPath.Replace('\\', '/') // 标准化
        };

        // 避免重复添加
        var existing = context.Manifest.ResolvedState.Assemblies
            .FirstOrDefault(a => a.Origin == AssetOrigin.Local && a.Id == localAsset.Id);

        if (existing == null)
        {
            context.Manifest.ResolvedState.Assemblies.Add(localAsset);
        }
        else
        {
            existing.RelativePath = localAsset.RelativePath; // 更新路径
        }

        // 4. 保存 (如果没有 SourcePath 则跳过，不影响内存状态)
        if (!string.IsNullOrEmpty(context.SourcePath))
        {
            try { await SaveProjectAsync(context); }
            catch (Exception ex) { Console.WriteLine($"[Warning] Save failed: {ex.Message}"); }
        }

        // 5. 刷新
        HydratePaths(context);
        await RoslynWorkspaceService.Instance.UpdateReferencesAsync(tabId, context.AbsoluteCompileReferences);
    }
    
    /// <summary>
    /// Remove Nuget reference
    /// </summary>
    public async Task RemovePackageAsync(string tabId, ProjectContext context, string packageId, CancellationToken ct = default)
    {
        if (!context.Config.NuGetPackages.Remove(packageId)) return;
        
        // 触发全量解析与保存 (自动移除未使用的依赖)
        // 因为这是一个全量计算过程，Resolver 会发现该包不在 Root 列表中了，
        // 自然也就不会把它（以及它的专属依赖）包含在生成的 Graph 中。
        var packageDto = new ScriptPackage 
        { 
            Config = context.Config, 
            Manifest = context.Manifest, 
            RootPath = context.EffectiveRootPath 
        };

        await ResolveAndSaveAsync(packageDto, context.SourcePath ?? context.EffectiveRootPath, ct);

        // 更新 Context
        context.Manifest = packageDto.Manifest;

        // 刷新环境
        HydratePaths(context);
        await RoslynWorkspaceService.Instance.UpdateReferencesAsync(tabId, context.AbsoluteCompileReferences);
    }

    public async Task RemoveReferenceAsync(string tabId, ProjectContext context, string referenceNameOrPath, CancellationToken ct = default)
    {
        // 1. 从 Config 中移除
        // 我们通过文件名或完整路径尝试匹配
        var configToRemove = context.Config.References
            .FirstOrDefault(r => r.Equals(referenceNameOrPath, StringComparison.OrdinalIgnoreCase) || 
                                 Path.GetFileName(r).Equals(Path.GetFileName(referenceNameOrPath), StringComparison.OrdinalIgnoreCase));

        if (configToRemove != null)
        {
            context.Config.References.Remove(configToRemove);
        }

        // 2. 从 Manifest 中移除
        // 本地引用在 Manifest 里的 ID 通常就是文件名
        var assetId = Path.GetFileName(referenceNameOrPath);
        var assetToRemove = context.Manifest.ResolvedState.Assemblies
            .FirstOrDefault(a => a.Origin == AssetOrigin.Local && a.Id.Equals(assetId, StringComparison.OrdinalIgnoreCase));

        if (assetToRemove != null)
        {
            context.Manifest.ResolvedState.Assemblies.Remove(assetToRemove);
        }

        // 3. 保存
        await SaveProjectAsync(context);

        // 4. 刷新
        HydratePaths(context);
        await RoslynWorkspaceService.Instance.UpdateReferencesAsync(tabId, context.AbsoluteCompileReferences);
    }
    // --- 核心私有逻辑 ---

    /// <summary>
    /// 执行全量解析、下载、提取，并将结果保存到磁盘。
    /// </summary>
    private async Task ResolveAndSaveAsync(ScriptPackage package, string originalPath, CancellationToken ct)
    {
        // A. 准备意图
        var rootPackages = package.Config.NuGetPackages
            .Select(kv => new PackageIdentity(kv.Key, NuGetVersion.Parse(kv.Value)))
            .ToList();

        // B. [大脑] 计算依赖图 (仅逻辑)
        // 这一步解决了版本冲突，拿到了一个扁平的包列表
        var graph = await DependencyResolver.Instance.ResolveFullGraphAsync(rootPackages,
            NuGetFramework.Parse("net8.0"), ct);

        // 清空旧状态
        package.Manifest ??= new PackageManifest();
        package.Manifest.ResolvedState.Assemblies.Clear();
        package.Manifest.ResolvedState.NativeAssets.Clear();

        // C. [工人] 下载并提取资产
        foreach (var identity in graph)
        {
            // 1. 确保物理文件存在
            var packagePath = await NuGetService.Instance.EnsurePackageDownloadedAsync(identity, ct);

            // 2. 智能提取 (ref/lib, runtimes)
            var assets = await NuGetService.Instance.GetPackageAssetsAsync(packagePath, NuGetFramework.Parse("net8.0"));

            // 3. 转换为 Manifest 格式 (相对路径化)
            // NuGet 资产: 存为包内相对路径 (e.g. "lib/net8.0/Json.dll")
            // 我们通过 Path.GetRelativePath 计算出它相对于 packagePath 的路径
            foreach (var absPath in assets.CompileReferences)
            {
                var relPath = Path.GetRelativePath(packagePath, absPath).Replace("\\", "/");

                package.Manifest.ResolvedState.Assemblies.Add(new ResolvedAsset
                {
                    Id = identity.Id,
                    Version = identity.Version.ToString(),
                    Origin = AssetOrigin.NuGet,
                    RelativePath = relPath
                });
            }

            if (assets.RuntimeNativePaths.Count != 0)
            {
                foreach (var rid in assets.RuntimeNativePaths)
                {
                    package.Manifest.ResolvedState.NativeAssets[rid.Key] = new();
                    foreach (var absPath in rid.Value)
                    {
                        var relPath = Path.GetRelativePath(packagePath, absPath).Replace("\\", "/");

                        package.Manifest.ResolvedState.NativeAssets[rid.Key].Add(
                            new ResolvedAsset()
                            {
                                Id = identity.Id,
                                Version = identity.Version.ToString(),
                                Origin = AssetOrigin.NuGet,
                                RelativePath = relPath
                            });
                    }
                }
            }
        }

        // D. 处理 Local 引用 (从 Config.References 中读取)
        // 只处理明确是文件路径的条目，跳过 BCL 程序集名 (如 "System.Runtime")
        foreach (var localRef in package.Config.References)
        {
            if (!localRef.Contains(Path.DirectorySeparatorChar) &&
                !localRef.Contains('/') &&
                !localRef.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                continue;

            package.Manifest.ResolvedState.Assemblies.Add(new ResolvedAsset
            {
                Id = Path.GetFileName(localRef),
                Origin = AssetOrigin.Local,
                RelativePath = localRef
            });
        }

        // E. [管家] 保存到磁盘
        await PackageService.Instance.SaveAsync(package, originalPath);
    }

    /// <summary>
    /// 补水逻辑：Manifest (相对) -> Context (绝对)
    /// </summary>
    private void HydratePaths(ProjectContext context)
    {
        context.AbsoluteCompileReferences.Clear();
        context.AbsoluteNativeAssets.Clear();

        foreach (var asset in context.Manifest.ResolvedState.Assemblies)
        {
            string absPath;
            if (asset.Origin == AssetOrigin.NuGet)
            {
                absPath = Path.Combine(globalPackagesFolder, asset.Id.ToLower(), asset.Version!.ToLower(),
                    asset.RelativePath);
            }
            else // Local
            {
                absPath = Path.Combine(context.EffectiveRootPath, asset.RelativePath);
            }

            if (File.Exists(absPath))
                context.AbsoluteCompileReferences.Add(absPath);
            else
                Console.WriteLine($"[Warning] Missing compile asset: {absPath}");
        }

        // Native Assets：按当前平台 RID 过滤
        var currentRid = GetCurrentRuntimeIdentifier();
        if (context.Manifest.ResolvedState.NativeAssets.TryGetValue(currentRid, out var nativeAssets))
        {
            foreach (var asset in nativeAssets)
            {
                string absPath;
                if (asset.Origin == AssetOrigin.NuGet)
                {
                    absPath = Path.Combine(globalPackagesFolder, asset.Id.ToLower(), asset.Version!.ToLower(),
                        asset.RelativePath);
                }
                else
                {
                    absPath = Path.Combine(context.EffectiveRootPath, asset.RelativePath);
                }

                if (File.Exists(absPath))
                    context.AbsoluteNativeAssets.Add(absPath);
                else
                    Console.WriteLine($"[Warning] Missing native asset: {absPath}");
            }
        }
    }

    private string GetCurrentRuntimeIdentifier() => RuntimeInformation.RuntimeIdentifier;
}