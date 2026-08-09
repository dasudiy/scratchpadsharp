using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ScratchpadSharp.Core.Configuration;
using ScratchpadSharp.Shared.Models;

namespace ScratchpadSharp.Core.Modules;

public sealed class ModuleCatalog
{
    private static readonly Lazy<ModuleCatalog> LazyInstance = new(() => new ModuleCatalog());
    public static ModuleCatalog Instance => LazyInstance.Value;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private ModuleCatalog()
    {
        AppPaths.EnsureUserDataDirectory();
    }

    public string GetInstanceDirectory(string instanceId) =>
        Path.Combine(AppPaths.ModulesDirectory, instanceId);

    public string GetModuleJsonPath(string instanceId) =>
        Path.Combine(GetInstanceDirectory(instanceId), "module.json");

    public string GetModelPath(string instanceId) =>
        Path.Combine(GetInstanceDirectory(instanceId), "model.cs");

    public IReadOnlyList<ModuleInstanceConfig> ListInstances(string? typeId = null)
    {
        if (!Directory.Exists(AppPaths.ModulesDirectory))
            return [];

        var list = new List<ModuleInstanceConfig>();
        foreach (var dir in Directory.EnumerateDirectories(AppPaths.ModulesDirectory))
        {
            var jsonPath = Path.Combine(dir, "module.json");
            if (!File.Exists(jsonPath))
                continue;

            try
            {
                var config = LoadConfigFromPath(jsonPath);
                if (typeId == null ||
                    string.Equals(config.TypeId, typeId, StringComparison.OrdinalIgnoreCase))
                    list.Add(config);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ModuleCatalog] Failed to load {jsonPath}: {ex.Message}");
            }
        }

        return list.OrderBy(c => c.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public ModuleInstanceConfig? TryGet(string instanceId)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
            return null;

        var path = GetModuleJsonPath(instanceId);
        if (!File.Exists(path))
            return null;

        return LoadConfigFromPath(path);
    }

    public string? ReadModelSource(string instanceId)
    {
        var path = GetModelPath(instanceId);
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    public void Save(ModuleInstanceConfig config, string modelSource)
    {
        var dir = GetInstanceDirectory(config.Id);
        Directory.CreateDirectory(dir);
        File.WriteAllText(GetModuleJsonPath(config.Id), JsonSerializer.Serialize(config, JsonOptions));
        File.WriteAllText(GetModelPath(config.Id), modelSource);
    }

    public void Delete(string instanceId)
    {
        var dir = GetInstanceDirectory(instanceId);
        if (Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);
    }

    private static ModuleInstanceConfig LoadConfigFromPath(string jsonPath)
    {
        var json = File.ReadAllText(jsonPath);
        var config = JsonSerializer.Deserialize<ModuleInstanceConfig>(json, JsonOptions)
                     ?? throw new InvalidDataException($"Invalid module.json: {jsonPath}");
        if (string.IsNullOrEmpty(config.Id))
            config.Id = Path.GetFileName(Path.GetDirectoryName(jsonPath) ?? string.Empty);
        return config;
    }
}
