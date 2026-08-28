using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using ScratchpadSharp.Shared.Models;

namespace ScratchpadSharp.Core.Services;

public static class MetadataReferenceProvider
{
    private static List<MetadataReference>? cachedReferences;

    private static MetadataReference CreateReferenceWithXmlDocs(string assemblyPath)
    {
        var docProvider = ResolveXmlDocumentation(assemblyPath);
        return MetadataReference.CreateFromFile(assemblyPath, documentation: docProvider);
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
        if (cachedReferences != null)
            return cachedReferences;

        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in GetFrameworkAssemblyPaths())
            paths.Add(path);

        paths.Add(typeof(ScriptExecutionService).Assembly.Location);

        cachedReferences = paths
            .Where(File.Exists)
            .Where(path => !string.Equals(Path.GetFileName(path), "Dumpify.dll", StringComparison.OrdinalIgnoreCase))
            .Select(CreateReferenceWithXmlDocs)
            .ToList();

        return cachedReferences;
    }

    /// <summary>
    /// All trusted platform assemblies for the current .NET runtime (includes facades like
    /// System.ComponentModel.TypeConverter). Falls back to the shared framework directory or a
    /// minimal type set when TPA is unavailable.
    /// </summary>
    private static IEnumerable<string> GetFrameworkAssemblyPaths()
    {
        var tpa = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        if (!string.IsNullOrWhiteSpace(tpa))
        {
            foreach (var path in tpa.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                if (path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    yield return path;
            }

            yield break;
        }

        var coreDir = Path.GetDirectoryName(typeof(object).Assembly.Location);
        if (!string.IsNullOrEmpty(coreDir) && Directory.Exists(coreDir))
        {
            foreach (var path in Directory.EnumerateFiles(coreDir, "*.dll", SearchOption.TopDirectoryOnly))
                yield return path;

            yield break;
        }

        yield return typeof(object).Assembly.Location;
        yield return typeof(Console).Assembly.Location;
        yield return typeof(Enumerable).Assembly.Location;
        yield return typeof(List<>).Assembly.Location;
        yield return typeof(Task).Assembly.Location;
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
            var name = Path.GetFileNameWithoutExtension(reference.Display ?? string.Empty);
            if (!string.IsNullOrEmpty(name))
                byName[name] = reference;
        }

        if (extraPaths == null || extraPaths.Count == 0)
            return byName.Values;

        foreach (var path in extraPaths)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                continue;

            var name = Path.GetFileNameWithoutExtension(path);
            if (string.IsNullOrEmpty(name))
                continue;

            byName[name] = CreateReferenceWithXmlDocs(path);
        }

        return byName.Values;
    }
}
