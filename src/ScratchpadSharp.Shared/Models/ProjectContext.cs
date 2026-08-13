using System;
using System.Collections.Generic;

namespace ScratchpadSharp.Shared.Models;

/// <summary>
/// 运行时项目上下文 (Hydrated Object)。
/// 包含已转换为绝对路径的资源列表，直接供 Roslyn 和 Runtime 使用。
/// </summary>
public class ProjectContext
{
    /// <summary>
    /// 项目原始路径 (可能是 .lqpkg 文件路径，也可能是文件夹路径)
    /// </summary>
    public string? SourcePath { get; set; } = string.Empty;

    /// <summary>
    /// 有效的物理根目录。
    /// 文件夹模式下 = SourcePath；
    /// Lqpkg 模式下 = 解压后的临时目录路径。
    /// </summary>
    public string EffectiveRootPath { get; set; } = string.Empty;

    /// <summary>
    /// 原始 Manifest 数据
    /// </summary>
    public PackageManifest Manifest { get; set; } = new();

    /// <summary>
    /// 用户配置 (意图)
    /// </summary>
    public ScriptConfig Config { get; set; } = new();

    /// <summary>
    /// [绝对路径] 用于编译的程序集列表
    /// </summary>
    public List<string> AbsoluteCompileReferences { get; set; } = new();

    /// <summary>
    /// [绝对路径] 用于脚本运行时加载的实现程序集（<c>lib/</c> / 平台 <c>runtimes/</c>，非 <c>ref/</c> 存根）
    /// </summary>
    public List<string> AbsoluteRuntimeReferences { get; set; } = new();

    /// <summary>
    /// [绝对路径] 用于运行时的 Native 库列表
    /// </summary>
    public List<string> AbsoluteNativeAssets { get; set; } = new();

    public string Code { get; set; }
    public string Output { get; set; }

    /// <summary>Merged environment (query + module refs) used for compile and IntelliSense.</summary>
    public MergedScriptEnvironment MergedEnvironment { get; set; } = new();

    public IReadOnlyList<string> EffectiveUsings =>
        MergedEnvironment.Usings.Count > 0 ? MergedEnvironment.Usings : Config.Usings;

    public void EnsureUsing(string ns)
    {
        if (string.IsNullOrWhiteSpace(ns) || ns == "<global namespace>")
            return;

        AddUsing(Config.Usings, ns);
        if (MergedEnvironment.Usings.Count > 0)
            AddUsing(MergedEnvironment.Usings, ns);
    }

    private static void AddUsing(List<string> usings, string ns)
    {
        foreach (var existing in usings)
        {
            if (string.Equals(existing, ns, StringComparison.Ordinal))
                return;
        }

        usings.Add(ns);
    }
}