using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Configuration;

namespace ScratchpadSharp.Core.Services;

public static class BclXmlResolver
{
    private static readonly Dictionary<string, string> _xmlCache = new();
    private static readonly List<string> _searchRoots = new();

    public static void Initialize(IConfiguration config)
    {
        _searchRoots.Clear();

        // 直接从 configuration 中读取。
        // 如果环境变量名为 DOTNET_PACKS_PATH，ConfigurationBuilder 默认会将其映射为同名 Key。
        // 如果在 appsettings.json 里有同名 Key，环境变量会根据加载顺序覆盖它（通常环境变量优先级更高）。
        var customPath = config["DOTNET_PACKS_PATH"];
        if (!string.IsNullOrEmpty(customPath))
        {
            _searchRoots.Add(customPath);
        }

        // 系统默认路径作为兜底
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            _searchRoots.Add("/usr/share/dotnet/packs/Microsoft.NETCore.App.Ref");
            _searchRoots.Add("/usr/lib/dotnet/packs/Microsoft.NETCore.App.Ref");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            _searchRoots.Add(@"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref");
        }
    }

    public static XmlDocumentationProvider? GetMetadataDocProvider(string assemblyName)
    {
        var name = Path.GetFileNameWithoutExtension(assemblyName);
        if (_xmlCache.TryGetValue(name, out var cachedPath))
            return XmlDocumentationProvider.CreateFromFile(cachedPath);

        foreach (var root in _searchRoots)
        {
            var path = FindXmlInRoot(root, name);
            if (path != null)
            {
                _xmlCache[name] = path;
                return XmlDocumentationProvider.CreateFromFile(path);
            }
        }
        return null;
    }

    private static string? FindXmlInRoot(string root, string assemblyName)
    {
        if (!Directory.Exists(root)) return null;

        try
        {
            // 扫描版本号文件夹，如 8.0.0
            var versionDir = Directory.GetDirectories(root)
                .OrderByDescending(d => d)
                .FirstOrDefault();

            if (versionDir == null) return null;

            // 递归查找 ref 目录下的 .xml (适应 net8.0, net7.0 等不同子目录)
            var refPath = Path.Combine(versionDir, "ref");
            if (!Directory.Exists(refPath)) return null;

            // 在所有 netX.X 子目录中搜索
            return Directory.EnumerateFiles(refPath, assemblyName + ".xml", SearchOption.AllDirectories)
                            .FirstOrDefault();
        }
        catch { return null; }
    }
}