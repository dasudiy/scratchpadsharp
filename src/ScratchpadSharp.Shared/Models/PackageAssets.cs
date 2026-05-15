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
    /// 用于运行时加载的原生库路径列表 (通常来自 runtimes/{rid}/native 目录)
    /// </summary>
    public Dictionary<string, List<string>> RuntimeNativePaths { get; init; } = new();

    public PackageAssets() { }

    public PackageAssets(List<string>? compileReferences, Dictionary<string, List<string>>? runtimeNativePaths)
    {
        CompileReferences = compileReferences ?? new List<string>();
        RuntimeNativePaths = runtimeNativePaths ?? new Dictionary<string, List<string>>();
    }
}