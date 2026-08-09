using System;
using System.Collections.Generic;
using System.Linq;
using ScratchpadSharp.Shared.Models;

namespace ScratchpadSharp.Core.Modules;

public static class ModuleMergeService
{
    public static MergedScriptEnvironment Merge(ScriptConfig queryConfig, IReadOnlyList<ModuleInstanceConfig> modules,
        IReadOnlyList<ModuleSourceFile> moduleSources)
    {
        var merged = new MergedScriptEnvironment
        {
            Usings = [..queryConfig.Usings],
            References = [..queryConfig.References],
            NuGetPackages = new Dictionary<string, string>(queryConfig.NuGetPackages),
            ModuleSources = moduleSources.ToList(),
            ResolvedModules = modules.ToList()
        };

        foreach (var module in modules)
        {
            foreach (var u in module.Usings)
            {
                if (!merged.Usings.Contains(u, StringComparer.Ordinal))
                    merged.Usings.Add(u);
            }

            foreach (var (id, version) in module.NuGetPackages)
            {
                if (merged.NuGetPackages.TryGetValue(id, out var existing) &&
                    !string.Equals(existing, version, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(
                        $"NuGet package version conflict for '{id}': query has {existing}, module '{module.DisplayName}' has {version}.");

                merged.NuGetPackages[id] = version;
            }
        }

        return merged;
    }

    public static MergedScriptEnvironment BuildFromQuery(ScriptConfig queryConfig)
    {
        var catalog = ModuleCatalog.Instance;
        var modules = new List<ModuleInstanceConfig>();
        var sources = new List<ModuleSourceFile>();

        foreach (var refId in queryConfig.ModuleRefs)
        {
            var instance = catalog.TryGet(refId);
            if (instance == null)
                throw new InvalidOperationException($"Module instance not found: {refId}");

            modules.Add(instance);
            var text = catalog.ReadModelSource(refId);
            if (string.IsNullOrWhiteSpace(text))
                throw new InvalidOperationException($"Module '{instance.DisplayName}' has no model.cs.");

            sources.Add(new ModuleSourceFile
            {
                FileName = $"Module_{instance.NamespaceSegment}_model.cs",
                SourceText = EnsureModuleUsings(text, instance.Usings)
            });
        }

        return Merge(queryConfig, modules, sources);
    }

    public static string EnsureModuleUsings(string source, IReadOnlyList<string> usings)
    {
        var missing = usings
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Where(u => !source.Contains($"using {u};", StringComparison.Ordinal))
            .ToList();

        if (missing.Count == 0)
            return source;

        var header = string.Join(Environment.NewLine, missing.Select(u => $"using {u};"));
        return header + Environment.NewLine + Environment.NewLine + source;
    }
}
