using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using ScratchpadSharp.Shared.Models;

namespace ScratchpadSharp.Core.Services;

public static class MetadataReferenceProvider
{
    private static List<MetadataReference>? cachedReferences;

    private static MetadataReference CreateReferenceWithXmlDocs(string assemblyPath)
    {
        var docProvider = BclXmlResolver.GetMetadataDocProvider(assemblyPath);
        return MetadataReference.CreateFromFile(assemblyPath, documentation: docProvider);
    }

    public static IEnumerable<MetadataReference> GetDefaultReferences()
    {
        if (cachedReferences != null)
            return cachedReferences;

        var references = new List<MetadataReference>
        {
            CreateReferenceWithXmlDocs(typeof(object).Assembly.Location),
            CreateReferenceWithXmlDocs(typeof(Console).Assembly.Location),
            CreateReferenceWithXmlDocs(typeof(Enumerable).Assembly.Location),
            CreateReferenceWithXmlDocs(typeof(List<>).Assembly.Location),
            CreateReferenceWithXmlDocs(typeof(Task).Assembly.Location),
            CreateReferenceWithXmlDocs(Assembly.Load("System.Runtime").Location),
            CreateReferenceWithXmlDocs(Assembly.Load("System.Collections").Location),
            CreateReferenceWithXmlDocs(Assembly.Load("System.Linq").Location),
            CreateReferenceWithXmlDocs(Assembly.Load("System.Linq.Expressions").Location),
            CreateReferenceWithXmlDocs(Assembly.Load("netstandard").Location),
            CreateReferenceWithXmlDocs(typeof(ScriptExecutionService).Assembly.Location),
        };

        // Add System.Private.CoreLib
        try
        {
            references.Add(CreateReferenceWithXmlDocs(Assembly.Load("System.Private.CoreLib").Location));
        }
        catch { }

        // Add common assemblies for better IntelliSense
        try
        {
            references.Add(CreateReferenceWithXmlDocs(Assembly.Load("System.Text.RegularExpressions").Location));
            references.Add(CreateReferenceWithXmlDocs(Assembly.Load("System.IO.FileSystem").Location));
            references.Add(CreateReferenceWithXmlDocs(Assembly.Load("System.Net.Http").Location));
        }
        catch { }

        cachedReferences = references;
        return references;
    }

    public static IEnumerable<MetadataReference> GetReferencesFromAssemblyNames(List<string> assemblyNames)
    {
        var references = new List<MetadataReference>();

        foreach (var assemblyName in assemblyNames)
        {
            try
            {
                var assembly = Assembly.Load(assemblyName);
                references.Add(MetadataReference.CreateFromFile(assembly.Location));
            }
            catch
            {
                // Skip assemblies that fail to load
            }
        }

        return references;
    }

    public static IEnumerable<MetadataReference> GetReferencesFromConfig(ScriptConfig config)
    {
        var references = new List<MetadataReference>();

        // Add core type references
        references.Add(MetadataReference.CreateFromFile(typeof(object).Assembly.Location));
        references.Add(MetadataReference.CreateFromFile(typeof(Console).Assembly.Location));
        references.Add(MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location));
        references.Add(MetadataReference.CreateFromFile(typeof(List<>).Assembly.Location));
        references.Add(MetadataReference.CreateFromFile(typeof(Task).Assembly.Location));

        // Add reference to ScratchpadSharp.Core to support DumpDispatcher
        references.Add(MetadataReference.CreateFromFile(typeof(ScriptExecutionService).Assembly.Location));

        // Add references from config
        if (config.DefaultReferences?.Count > 0)
        {
            references.AddRange(GetReferencesFromAssemblyNames(config.DefaultReferences));
        }

        // Add NuGet packages
        if (config.NuGetPackages?.Count > 0)
        {
            references.AddRange(GetPackageReferences(config.NuGetPackages));
        }

        return references;
    }

    public static IEnumerable<MetadataReference> GetReferencesWithPackages(Dictionary<string, string> nugetPackages)
    {
        var references = GetDefaultReferences().ToList();

        if (nugetPackages == null || nugetPackages.Count == 0)
            return references;

        references.AddRange(GetPackageReferences(nugetPackages));
        return references;
    }

    private static IEnumerable<MetadataReference> GetPackageReferences(Dictionary<string, string> nugetPackages)
    {
        var references = new List<MetadataReference>();
        var nugetPackagesPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".nuget", "packages");

        foreach (var package in nugetPackages)
        {
            try
            {
                var packageName = package.Key.ToLowerInvariant();
                var version = package.Value;
                var packagePath = Path.Combine(nugetPackagesPath, packageName, version);

                if (Directory.Exists(packagePath))
                {
                    var libPath = Path.Combine(packagePath, "lib");
                    if (Directory.Exists(libPath))
                    {
                        var dllFiles = Directory.GetFiles(libPath, "*.dll", SearchOption.AllDirectories)
                            .Where(f => !f.Contains("\\ref\\"))
                            .ToList();

                        foreach (var dll in dllFiles)
                        {
                            try
                            {
                                references.Add(MetadataReference.CreateFromFile(dll));
                            }
                            catch { }
                        }
                    }
                }
            }
            catch { }
        }

        return references;
    }
}
