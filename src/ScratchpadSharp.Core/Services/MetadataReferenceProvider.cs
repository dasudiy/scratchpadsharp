using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.CodeAnalysis;
using ScratchpadSharp.Shared.Models;

namespace ScratchpadSharp.Core.Services;

public static class MetadataReferenceProvider
{
    private static List<MetadataReference>? cachedReferences;

    // MetadataReference is immutable and safely shared across projects/compilations. Entries are
    // keyed by path and invalidated when last-write time or length changes so a rebuilt local DLL
    // is re-read and the previous mapping can be released.
    private static readonly ConcurrentDictionary<string, CachedReference> referenceCache =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly record struct CachedReference(
        long LastWriteTicks, long Length, MetadataReference Reference);

    private static MetadataReference CreateReferenceWithXmlDocs(string assemblyPath)
    {
        var (lastWrite, length) = ReadStamp(assemblyPath);
        if (referenceCache.TryGetValue(assemblyPath, out var cached) &&
            cached.LastWriteTicks == lastWrite &&
            cached.Length == length)
            return cached.Reference;

        var reference = MetadataReference.CreateFromFile(
            assemblyPath, documentation: ResolveXmlDocumentation(assemblyPath));
        referenceCache[assemblyPath] = new CachedReference(lastWrite, length, reference);
        return reference;
    }

    private static (long LastWriteTicks, long Length) ReadStamp(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? (info.LastWriteTimeUtc.Ticks, info.Length) : (0, 0);
        }
        catch
        {
            return (0, 0);
        }
    }

    private static XmlDocumentationProvider? ResolveXmlDocumentation(string assemblyPath)
    {
        var docProvider = BclXmlResolver.GetMetadataDocProvider(assemblyPath);
        if (docProvider != null)
            return docProvider;

        var siblingXml = Path.ChangeExtension(assemblyPath, ".xml");
        return File.Exists(siblingXml) ? XmlDocumentationProvider.CreateFromFile(siblingXml) : null;
    }

    /// <summary>
    /// Baseline references for script compilation: shared framework (TPA) + ScratchpadSharp.Core.
    /// NuGet compile assets are added separately via <see cref="GetReferencesWithPackages"/>.
    /// </summary>
    public static IEnumerable<MetadataReference> GetDefaultReferences()
    {
        if (cachedReferences is { Count: >= MinimumFrameworkReferenceCount })
            return cachedReferences;

        var built = BuildDefaultReferences();
        if (built.Count >= MinimumFrameworkReferenceCount)
            cachedReferences = built;

        return built;
    }

    private static List<MetadataReference> BuildDefaultReferences()
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in GetFrameworkAssemblyPaths())
            paths.Add(path);

        foreach (var assembly in new[] { typeof(ScriptExecutionService).Assembly, typeof(ScriptConfig).Assembly })
        {
            var location = assembly.Location;
            if (!string.IsNullOrWhiteSpace(location))
            {
                paths.Add(location);
                continue;
            }

            var sibling = Path.Combine(AppContext.BaseDirectory, assembly.GetName().Name + ".dll");
            paths.Add(sibling);
        }

        return CreateReferences(paths);
    }

    private const int MinimumFrameworkReferenceCount = 50;

    private static List<MetadataReference> CreateReferences(IEnumerable<string> paths) =>
        paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Where(File.Exists)
            .Where(IsManagedAssembly)
            .Where(path => !string.Equals(Path.GetFileName(path), "Dumpify.dll", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(CreateReferenceWithXmlDocs)
            .ToList();

    /// <summary>
    /// Managed framework assemblies for script compilation. Prefer TRUSTED_PLATFORM_ASSEMBLIES;
    /// fall back to the installed shared framework directory when TPA is unavailable.
    /// </summary>
    private static IEnumerable<string> GetFrameworkAssemblyPaths()
    {
        var tpa = ReadExistingTrustedPlatformAssemblyPaths();
        if (tpa.Count >= MinimumFrameworkReferenceCount)
            return tpa;

        var paths = new HashSet<string>(tpa, StringComparer.OrdinalIgnoreCase);
        foreach (var path in SharedFrameworkResolver.GetMicrosoftNetCoreAppAssemblyPaths())
            paths.Add(path);

        if (paths.Count >= MinimumFrameworkReferenceCount)
            return paths;

        foreach (var path in GetRuntimeDirectoryAssemblyPaths())
            paths.Add(path);

        return paths;
    }

    private static List<string> ReadExistingTrustedPlatformAssemblyPaths()
    {
        var paths = new List<string>();
        var tpa = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        if (string.IsNullOrWhiteSpace(tpa))
            return paths;

        foreach (var path in tpa.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
                continue;

            if (IsManagedAssembly(path))
                paths.Add(path);
        }

        return paths;
    }

    private static IEnumerable<string> GetRuntimeDirectoryAssemblyPaths()
    {
        var runtimeDir = RuntimeEnvironment.GetRuntimeDirectory();
        if (string.IsNullOrEmpty(runtimeDir) || !Directory.Exists(runtimeDir))
            yield break;

        foreach (var path in Directory.EnumerateFiles(runtimeDir, "*.dll", SearchOption.TopDirectoryOnly))
        {
            if (IsManagedAssembly(path))
                yield return path;
        }
    }

    public static bool IsManagedAssembly(string path)
    {
        try
        {
            AssemblyName.GetAssemblyName(path);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Default framework refs plus extra compile assets. Extra paths replace a default
    /// reference with the same assembly simple name so IntelliSense matches script runtime.
    /// </summary>
    public static IEnumerable<MetadataReference> GetReferencesWithPackages(List<string>? extraPaths)
    {
        var byName = new Dictionary<string, MetadataReference>(StringComparer.OrdinalIgnoreCase);

        foreach (var reference in GetDefaultReferences())
        {
            var name = GetReferenceAssemblyName(reference);
            if (!string.IsNullOrEmpty(name))
                byName[name] = reference;
        }

        if (extraPaths == null || extraPaths.Count == 0)
            return byName.Values;

        foreach (var path in extraPaths)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path) || !IsManagedAssembly(path))
                continue;

            var name = Path.GetFileNameWithoutExtension(path);
            if (string.IsNullOrEmpty(name))
                continue;

            byName[name] = CreateReferenceWithXmlDocs(path);
        }

        return byName.Values;
    }

    private static string GetReferenceAssemblyName(MetadataReference reference)
    {
        var path = reference switch
        {
            PortableExecutableReference portable when !string.IsNullOrWhiteSpace(portable.FilePath)
                => portable.FilePath,
            _ => reference.Display
        };

        return Path.GetFileNameWithoutExtension(path ?? string.Empty);
    }
}
