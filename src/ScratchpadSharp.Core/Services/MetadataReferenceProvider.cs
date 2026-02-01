using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;

namespace ScratchpadSharp.Core.Services;

public static class MetadataReferenceProvider
{
    private static List<MetadataReference>? cachedReferences;

    public static IEnumerable<MetadataReference> GetDefaultReferences()
    {
        if (cachedReferences != null)
            return cachedReferences;

        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(List<>).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Task).Assembly.Location),
            MetadataReference.CreateFromFile(Assembly.Load("System.Runtime").Location),
            MetadataReference.CreateFromFile(Assembly.Load("System.Collections").Location),
            MetadataReference.CreateFromFile(Assembly.Load("System.Linq").Location),
            MetadataReference.CreateFromFile(Assembly.Load("System.Linq.Expressions").Location),
            MetadataReference.CreateFromFile(Assembly.Load("netstandard").Location),
        };

        // Add System.Private.CoreLib
        try
        {
            references.Add(MetadataReference.CreateFromFile(Assembly.Load("System.Private.CoreLib").Location));
        }
        catch { }

        // Add common assemblies for better IntelliSense
        try
        {
            references.Add(MetadataReference.CreateFromFile(Assembly.Load("System.Text.RegularExpressions").Location));
            references.Add(MetadataReference.CreateFromFile(Assembly.Load("System.IO.FileSystem").Location));
            references.Add(MetadataReference.CreateFromFile(Assembly.Load("System.Net.Http").Location));
        }
        catch { }

        cachedReferences = references;
        return references;
    }

    public static IEnumerable<MetadataReference> GetReferencesWithPackages(Dictionary<string, string> nugetPackages)
    {
        var references = GetDefaultReferences().ToList();

        if (nugetPackages == null || nugetPackages.Count == 0)
            return references;

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
                    // Find all DLL files in lib folders
                    var libPath = Path.Combine(packagePath, "lib");
                    if (Directory.Exists(libPath))
                    {
                        var dllFiles = Directory.GetFiles(libPath, "*.dll", SearchOption.AllDirectories)
                            .Where(f => !f.Contains("\\ref\\")) // Skip reference assemblies
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
            catch
            {
                // Skip packages that fail to load
            }
        }

        return references;
    }
}
