using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

namespace ScratchpadSharp.Core.PackageManagement;

/// <summary>
/// Maps NuGet compile assets (often <c>ref/</c>) to implementation assemblies for runtime loading.
/// </summary>
public static class NuGetPackageAssetResolver
{
    public static bool IsReferenceAssemblyPath(string? path) =>
        !string.IsNullOrWhiteSpace(path) &&
        path.Replace('\\', '/').Contains("/ref/", StringComparison.OrdinalIgnoreCase);

    public static string EnsureImplementationAssemblyPath(string assemblyPath)
    {
        var resolved = ResolveRuntimeAssemblyPath(assemblyPath);
        if (IsReferenceAssemblyPath(resolved))
            throw new InvalidOperationException(
                $"NuGet reference assembly cannot be loaded at runtime: {resolved}");

        return resolved;
    }

    public static string ResolveRuntimeAssemblyPath(string assemblyPath)
    {
        if (string.IsNullOrWhiteSpace(assemblyPath) || !File.Exists(assemblyPath))
            return assemblyPath;

        var packageRoot = InferPackageRoot(assemblyPath);
        if (packageRoot == null)
            return assemblyPath;

        var rel = Path.GetRelativePath(packageRoot, assemblyPath).Replace('\\', '/');
        if (!TryParsePackageLibRelativePath(rel, out var tfm, out var fileName))
            return assemblyPath;

        var platformLib = TryResolvePlatformLib(packageRoot, tfm, fileName);
        if (platformLib != null)
            return platformLib;

        if (rel.Contains("/ref/", StringComparison.OrdinalIgnoreCase))
        {
            var libPath = Path.Combine(packageRoot, "lib", tfm, fileName);
            return File.Exists(libPath) ? libPath : assemblyPath;
        }

        return assemblyPath;
    }

    /// <summary>
    /// Collapses duplicate runtime DLLs (ref/lib/runtimes) to one path per assembly name,
    /// preferring <c>runtimes/{os}/lib</c> over plain <c>lib/</c> over <c>ref/</c>.
    /// </summary>
    public static List<string> SelectPreferredRuntimeAssemblies(IEnumerable<string> assemblyPaths)
    {
        var best = new Dictionary<string, (string Path, int Priority)>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in assemblyPaths)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                continue;

            try
            {
                var impl = EnsureImplementationAssemblyPath(path);
                var name = AssemblyName.GetAssemblyName(impl).Name
                           ?? Path.GetFileNameWithoutExtension(impl);
                var priority = RuntimeAssemblyPathPriority(impl);

                if (!best.TryGetValue(name, out var existing) || priority > existing.Priority)
                    best[name] = (impl, priority);
            }
            catch
            {
                // skip invalid paths
            }
        }

        return best.Values.Select(v => v.Path).ToList();
    }

    public static int RuntimeAssemblyPathPriority(string path)
    {
        var normalized = path.Replace('\\', '/');
        if (normalized.Contains("/ref/", StringComparison.OrdinalIgnoreCase))
            return 0;

        if (normalized.Contains("/runtimes/", StringComparison.OrdinalIgnoreCase) &&
            normalized.Contains("/lib/", StringComparison.OrdinalIgnoreCase))
            return 3;

        if (normalized.Contains("/lib/", StringComparison.OrdinalIgnoreCase))
            return 1;

        return 2;
    }

    private static string? TryResolvePlatformLib(string packageRoot, string tfm, string fileName)
    {
        foreach (var osFolder in GetRuntimeOsFolders())
        {
            var platformLib = Path.Combine(packageRoot, "runtimes", osFolder, "lib", tfm, fileName);
            if (File.Exists(platformLib))
                return platformLib;
        }

        return null;
    }

    private static bool TryParsePackageLibRelativePath(string rel, out string tfm, out string fileName)
    {
        tfm = string.Empty;
        fileName = string.Empty;

        if (rel.Contains("/ref/", StringComparison.OrdinalIgnoreCase))
        {
            var segments = rel.Split('/');
            if (segments.Length < 3)
                return false;

            tfm = segments[1];
            fileName = segments[^1];
            return true;
        }

        if (rel.StartsWith("lib/", StringComparison.OrdinalIgnoreCase))
        {
            var segments = rel.Split('/');
            if (segments.Length < 3)
                return false;

            tfm = segments[1];
            fileName = segments[^1];
            return true;
        }

        return false;
    }

    /// <summary>NuGet package root (the folder that contains <c>ref/</c> or <c>lib/</c>).</summary>
    public static string? InferPackageRoot(string assemblyPath)
    {
        var normalized = assemblyPath.Replace('\\', '/');
        var refIdx = normalized.IndexOf("/ref/", StringComparison.OrdinalIgnoreCase);
        if (refIdx > 0)
            return normalized[..refIdx].Replace('/', Path.DirectorySeparatorChar);

        var libIdx = normalized.IndexOf("/lib/", StringComparison.OrdinalIgnoreCase);
        if (libIdx > 0)
            return normalized[..libIdx].Replace('/', Path.DirectorySeparatorChar);

        var runtimesIdx = normalized.IndexOf("/runtimes/", StringComparison.OrdinalIgnoreCase);
        if (runtimesIdx > 0)
            return normalized[..runtimesIdx].Replace('/', Path.DirectorySeparatorChar);

        return null;
    }

    public static IEnumerable<string> GetRuntimeOsFolders()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            yield return "win";
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            yield return "osx";
            yield return "unix";
        }
        else
        {
            yield return "unix";
            yield return "linux";
        }
    }

    public static string CombinePackageRelativePath(string packageRoot, string relativePath) =>
        Path.Combine(packageRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
}
