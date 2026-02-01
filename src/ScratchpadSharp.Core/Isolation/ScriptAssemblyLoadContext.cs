using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

namespace ScratchpadSharp.Core.Isolation;

/// <summary>
/// AssemblyLoadContext for isolating script execution and enabling unloading.
/// Supports collectible assemblies with native library resolution for Linux.
/// </summary>
public class ScriptAssemblyLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver? resolver;
    private readonly List<string> additionalProbingPaths;

    public ScriptAssemblyLoadContext(string? assemblyPath = null, List<string>? additionalPaths = null) 
        : base(isCollectible: true)
    {
        resolver = assemblyPath != null ? new AssemblyDependencyResolver(assemblyPath) : null;
        additionalProbingPaths = additionalPaths ?? new List<string>();
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        // Try to resolve using the dependency resolver first
        if (resolver != null)
        {
            string? assemblyPath = resolver.ResolveAssemblyToPath(assemblyName);
            if (assemblyPath != null)
            {
                return LoadFromAssemblyPath(assemblyPath);
            }
        }

        // Try additional probing paths
        foreach (var probingPath in additionalProbingPaths)
        {
            var candidatePath = Path.Combine(probingPath, $"{assemblyName.Name}.dll");
            if (File.Exists(candidatePath))
            {
                return LoadFromAssemblyPath(candidatePath);
            }
        }

        // Let the default context handle it (framework assemblies)
        return null;
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        // Handle native library resolution, especially for Linux .so files
        if (resolver != null)
        {
            string? libraryPath = resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
            if (libraryPath != null)
            {
                return LoadUnmanagedDllFromPath(libraryPath);
            }
        }

        // Try to find native libraries in runtimes folders
        var rid = GetRuntimeIdentifier();
        var possibleNames = GetPossibleNativeLibraryNames(unmanagedDllName);

        foreach (var probingPath in additionalProbingPaths)
        {
            // Look in runtimes/{rid}/native/ structure
            var runtimesPath = Path.Combine(probingPath, "runtimes", rid, "native");
            if (Directory.Exists(runtimesPath))
            {
                foreach (var name in possibleNames)
                {
                    var candidatePath = Path.Combine(runtimesPath, name);
                    if (File.Exists(candidatePath))
                    {
                        return LoadUnmanagedDllFromPath(candidatePath);
                    }
                }
            }

            // Also try direct path
            foreach (var name in possibleNames)
            {
                var candidatePath = Path.Combine(probingPath, name);
                if (File.Exists(candidatePath))
                {
                    return LoadUnmanagedDllFromPath(candidatePath);
                }
            }
        }

        // Let the default resolution handle it
        return IntPtr.Zero;
    }

    private static string GetRuntimeIdentifier()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            // Common Linux RIDs
            return RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 => "linux-x64",
                Architecture.Arm64 => "linux-arm64",
                Architecture.Arm => "linux-arm",
                _ => "linux-x64"
            };
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 => "win-x64",
                Architecture.X86 => "win-x86",
                Architecture.Arm64 => "win-arm64",
                _ => "win-x64"
            };
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 => "osx-x64",
                Architecture.Arm64 => "osx-arm64",
                _ => "osx-x64"
            };
        }

        return "linux-x64"; // Default fallback
    }

    private static IEnumerable<string> GetPossibleNativeLibraryNames(string unmanagedDllName)
    {
        yield return unmanagedDllName;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            // Try .so variants
            if (!unmanagedDllName.EndsWith(".so"))
            {
                yield return $"lib{unmanagedDllName}.so";
                yield return $"{unmanagedDllName}.so";
            }
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Try .dll variants
            if (!unmanagedDllName.EndsWith(".dll"))
            {
                yield return $"{unmanagedDllName}.dll";
            }
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            // Try .dylib variants
            if (!unmanagedDllName.EndsWith(".dylib"))
            {
                yield return $"lib{unmanagedDllName}.dylib";
                yield return $"{unmanagedDllName}.dylib";
            }
        }
    }
}
