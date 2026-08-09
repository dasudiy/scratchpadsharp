using System.Collections.Generic;

namespace ScratchpadSharp.Shared.Models;

/// <summary>
/// 表示从 NuGet 包中提取出的物理资产路径集合。
/// </summary>
public record PackageAssets
{
    /// <summary>
    /// 用于 Roslyn 编译的程序集路径列表 (通常来自 ref 或 lib 目录)
    /// </summary>
    public List<string> CompileReferences { get; init; } = new();

    /// <summary>
    /// 用于脚本运行时加载的实现程序集（<c>lib/</c> 与 <c>runtimes/{os}/lib/</c>，非 <c>ref/</c> 存根）
    /// </summary>
    public List<string> RuntimeAssemblyReferences { get; init; } = new();

    /// <summary>
    /// 用于运行时加载的原生库路径列表 (通常来自 runtimes/{rid}/native 目录)
    /// </summary>
    public Dictionary<string, List<string>> RuntimeNativePaths { get; init; } = new();

    public PackageAssets() { }

    public PackageAssets(
        List<string>? compileReferences,
        List<string>? runtimeAssemblyReferences,
        Dictionary<string, List<string>>? runtimeNativePaths)
    {
        CompileReferences = compileReferences ?? new List<string>();
        RuntimeAssemblyReferences = runtimeAssemblyReferences ?? new List<string>();
        RuntimeNativePaths = runtimeNativePaths ?? new Dictionary<string, List<string>>();
    }
}