using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using ScratchpadSharp.Core.PackageManagement;

namespace ScratchpadSharp.Core.Isolation;

/// <summary>
/// AssemblyLoadContext for isolating script execution and enabling unloading.
/// Supports collectible assemblies with native library resolution for Linux.
/// </summary>
public class ScriptAssemblyLoadContext : AssemblyLoadContext
{
    private static readonly string[] PreloadAssemblyOrder =
    [
        "Microsoft.Data.SqlClient",
        "Microsoft.EntityFrameworkCore.Abstractions",
        "Microsoft.EntityFrameworkCore",
        "Microsoft.EntityFrameworkCore.Relational",
        "Microsoft.EntityFrameworkCore.SqlServer"
    ];

    private readonly List<AssemblyDependencyResolver> resolvers = [];
    private readonly HashSet<string> resolverAssemblyPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> additionalProbingPaths;
    private readonly Dictionary<string, string> runtimeReferencePaths;

    /// <param name="runtimeReferences">
    /// Implementation assembly paths for the script ALC (typically
    /// <c>AbsoluteRuntimeReferences</c>: <c>lib/</c> or <c>runtimes/{os}/lib/</c>, never <c>ref/</c> stubs).
    /// </param>
    public ScriptAssemblyLoadContext(
        string? assemblyPath = null,
        List<string>? additionalPaths = null,
        IEnumerable<string>? runtimeReferences = null)
        : base(isCollectible: true)
    {
        additionalProbingPaths = additionalPaths is { Count: > 0 } ? [..additionalPaths] : [];
        runtimeReferencePaths = BuildRuntimeReferenceMap(runtimeReferences);

        TryAddResolver(assemblyPath);
        if (runtimeReferences != null)
        {
            foreach (var path in runtimeReferences)
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                    continue;

                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir) &&
                    !additionalProbingPaths.Contains(dir, StringComparer.OrdinalIgnoreCase))
                    additionalProbingPaths.Add(dir);

                if (NuGetPackageAssetResolver.InferPackageRoot(path) == null)
                    TryAddResolver(path);
            }
        }

        PreloadRuntimeAssemblies(runtimeReferences);
    }

    private void TryAddResolver(string? assemblyPath)
    {
        if (string.IsNullOrWhiteSpace(assemblyPath) || !File.Exists(assemblyPath))
            return;

        var depsPath = Path.Combine(
            Path.GetDirectoryName(assemblyPath)!,
            Path.GetFileNameWithoutExtension(assemblyPath) + ".deps.json");
        if (!File.Exists(depsPath))
            return;

        if (!resolverAssemblyPaths.Add(Path.GetFullPath(assemblyPath)))
            return;

        try
        {
            resolvers.Add(new AssemblyDependencyResolver(assemblyPath));
        }
        catch
        {
            resolverAssemblyPaths.Remove(Path.GetFullPath(assemblyPath));
            // deps.json present but not usable as a component manifest
        }
    }

    private void PreloadRuntimeAssemblies(IEnumerable<string>? runtimeReferences)
    {
        if (runtimeReferences == null)
            return;

        var paths = NuGetPackageAssetResolver.SelectPreferredRuntimeAssemblies(
            runtimeReferences.Where(p => !string.IsNullOrWhiteSpace(p)));

        foreach (var assemblyName in PreloadAssemblyOrder)
        {
            var path = paths.FirstOrDefault(p =>
                string.Equals(Path.GetFileNameWithoutExtension(p), assemblyName, StringComparison.OrdinalIgnoreCase));
            if (path != null)
                TryLoadAssembly(path);
        }

        foreach (var path in paths)
            TryLoadAssembly(path);
    }

    private void TryLoadAssembly(string path)
    {
        if (NuGetPackageAssetResolver.IsReferenceAssemblyPath(path))
            return;

        try
        {
            var assembly = LoadFromAssemblyPath(path);
            var name = assembly.GetName().Name;
            if (!string.IsNullOrEmpty(name))
                runtimeReferencePaths[name] = path;
        }
        catch
        {
            // skip assemblies that fail to load
        }
    }

    private static Dictionary<string, string> BuildRuntimeReferenceMap(IEnumerable<string>? runtimeReferences)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (runtimeReferences == null)
            return map;

        foreach (var runtimePath in NuGetPackageAssetResolver.SelectPreferredRuntimeAssemblies(
                     runtimeReferences.Where(p => !string.IsNullOrWhiteSpace(p))))
        {
            try
            {
                var name = AssemblyName.GetAssemblyName(runtimePath).Name;
                if (!string.IsNullOrEmpty(name))
                    map[name] = runtimePath;
            }
            catch
            {
                // skip assemblies that fail to load
            }
        }

        return map;
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        foreach (var resolver in resolvers)
        {
            var assemblyPath = resolver.ResolveAssemblyToPath(assemblyName);
            if (assemblyPath != null)
                return LoadFromAssemblyPath(assemblyPath);
        }

        if (assemblyName.Name != null &&
            runtimeReferencePaths.TryGetValue(assemblyName.Name, out var mappedPath))
        {
            return LoadFromAssemblyPath(mappedPath);
        }

        foreach (var probingPath in additionalProbingPaths)
        {
            if (!Directory.Exists(probingPath))
                continue;

            foreach (var osFolder in NuGetPackageAssetResolver.GetRuntimeOsFolders())
            {
                foreach (var tfm in new[] { "net8.0", "net6.0", "netstandard2.1", "netstandard2.0" })
                {
                    var candidatePath = Path.Combine(probingPath, "runtimes", osFolder, "lib", tfm,
                        $"{assemblyName.Name}.dll");
                    if (File.Exists(candidatePath))
                        return LoadFromAssemblyPath(candidatePath);
                }
            }
        }

        foreach (var probingPath in additionalProbingPaths)
        {
            var candidatePath = Path.Combine(probingPath, $"{assemblyName.Name}.dll");
            if (File.Exists(candidatePath))
                return LoadFromAssemblyPath(candidatePath);
        }

        // Framework assemblies only — never fall back to Default for NuGet package assemblies
        return null;
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        foreach (var resolver in resolvers)
        {
            var libraryPath = resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
            if (libraryPath != null)
                return LoadUnmanagedDllFromPath(libraryPath);
        }

        var rid = GetRuntimeIdentifier();
        var possibleNames = GetPossibleNativeLibraryNames(unmanagedDllName);

        foreach (var probingPath in additionalProbingPaths)
        {
            if (File.Exists(probingPath) && IsNativeLibraryFile(probingPath))
            {
                foreach (var name in possibleNames)
                {
                    if (string.Equals(Path.GetFileName(probingPath), name, StringComparison.OrdinalIgnoreCase))
                        return LoadUnmanagedDllFromPath(probingPath);
                }
            }

            var runtimesPath = Path.Combine(probingPath, "runtimes", rid, "native");
            if (Directory.Exists(runtimesPath))
            {
                foreach (var name in possibleNames)
                {
                    var candidatePath = Path.Combine(runtimesPath, name);
                    if (File.Exists(candidatePath))
                        return LoadUnmanagedDllFromPath(candidatePath);
                }
            }

            foreach (var name in possibleNames)
            {
                var candidatePath = Path.Combine(probingPath, name);
                if (File.Exists(candidatePath))
                    return LoadUnmanagedDllFromPath(candidatePath);
            }
        }

        return IntPtr.Zero;
    }

    private static bool IsNativeLibraryFile(string path) =>
        path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".so", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".dylib", StringComparison.OrdinalIgnoreCase);

    private static string GetRuntimeIdentifier() =>
        RuntimeInformation.RuntimeIdentifier;

    private static IEnumerable<string> GetPossibleNativeLibraryNames(string unmanagedDllName)
    {
        yield return unmanagedDllName;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            if (!unmanagedDllName.EndsWith(".so", StringComparison.OrdinalIgnoreCase))
            {
                yield return $"lib{unmanagedDllName}.so";
                yield return $"{unmanagedDllName}.so";
            }
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            if (!unmanagedDllName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                yield return $"{unmanagedDllName}.dll";
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            if (!unmanagedDllName.EndsWith(".dylib", StringComparison.OrdinalIgnoreCase))
            {
                yield return $"lib{unmanagedDllName}.dylib";
                yield return $"{unmanagedDllName}.dylib";
            }
        }
    }
}
