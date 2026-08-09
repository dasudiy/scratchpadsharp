using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ScratchpadSharp.Core.Database;
using ScratchpadSharp.Shared.Models;

namespace ScratchpadSharp.Core.Modules;

public sealed class EfCoreModuleFactory
{
  public static EfCoreModuleFactory Instance { get; } = new();

  private EfCoreModuleFactory()
  {
  }

  public static string SanitizeNamespaceSegment(string displayName)
  {
    var cleaned = Regex.Replace(displayName ?? string.Empty, @"[^\w]+", "_");
    if (string.IsNullOrEmpty(cleaned))
      return "Database";

    var parts = cleaned.Split('_', StringSplitOptions.RemoveEmptyEntries);
    var pascal = string.Concat(parts.Select(p =>
      char.ToUpperInvariant(p[0]) + (p.Length > 1 ? p[1..] : string.Empty)));

    if (string.IsNullOrEmpty(pascal))
      return "Database";
    if (char.IsDigit(pascal[0]))
      return "T" + pascal;
    return pascal;
  }

  public ModuleInstanceConfig CreateConfig(string displayName, string providerId, string connectionString)
  {
    var provider = DatabaseProviderCatalog.Get(providerId);
    if (provider.Id == DatabaseProviderIds.None)
      throw new ArgumentException("Select a database provider.", nameof(providerId));

    var id = Guid.NewGuid().ToString("N");
    var segment = SanitizeNamespaceSegment(displayName);
    var config = new ModuleInstanceConfig
    {
      Id = id,
      TypeId = ModuleTypeIds.EfCore,
      DisplayName = displayName.Trim(),
      NamespaceSegment = segment,
      ProviderId = provider.Id,
      ConnectionString = connectionString,
      Usings = ["System", "Microsoft.EntityFrameworkCore"]
    };

    DatabaseProviderCatalog.ApplyModulePackages(config);
    return config;
  }

  public async Task<ModuleInstanceConfig> CreateInstanceAsync(string displayName, string providerId,
    string connectionString, CancellationToken ct = default)
  {
    var config = CreateConfig(displayName, providerId, connectionString);
    var provider = DatabaseProviderCatalog.Get(providerId);
    var schemaProvider = DbSchemaProviderFactory.Create(provider.Id);
    var snapshot = await schemaProvider.GetSchemaAsync(connectionString, ct);
    var model = EfScaffoldGenerator.GenerateModel(snapshot, provider, config.NamespaceSegment, connectionString);
    ModuleCatalog.Instance.Save(config, model);
    return config;
  }

  public async Task<ModuleInstanceConfig> UpdateConnectionAsync(ModuleInstanceConfig config, string providerId,
    string connectionString, CancellationToken ct = default)
  {
    config.ProviderId = DatabaseProviderCatalog.Get(providerId).Id;
    config.ConnectionString = connectionString;
    DatabaseProviderCatalog.ApplyModulePackages(config);
    ModuleCatalog.Instance.Save(config, ModuleCatalog.Instance.ReadModelSource(config.Id) ?? string.Empty);
    return config;
  }

  public async Task RegenerateModelAsync(string instanceId, CancellationToken ct = default)
  {
    var config = ModuleCatalog.Instance.TryGet(instanceId)
                 ?? throw new InvalidOperationException($"Module not found: {instanceId}");

    var provider = DatabaseProviderCatalog.Get(config.ProviderId);
    var schemaProvider = DbSchemaProviderFactory.Create(provider.Id);
    var snapshot = await schemaProvider.GetSchemaAsync(config.ConnectionString, ct);
    var model = EfScaffoldGenerator.GenerateModel(snapshot, provider, config.NamespaceSegment, config.ConnectionString);
    ModuleCatalog.Instance.Save(config, model);
  }

  public async Task<DbSchemaSnapshot> GetSchemaAsync(string instanceId, CancellationToken ct = default)
  {
    var config = ModuleCatalog.Instance.TryGet(instanceId)
                 ?? throw new InvalidOperationException($"Module not found: {instanceId}");
    var provider = DbSchemaProviderFactory.Create(config.ProviderId);
    return await provider.GetSchemaAsync(config.ConnectionString, ct);
  }

  public async Task<ConnectionTestResult> TestConnectionAsync(string providerId, string connectionString,
    CancellationToken ct = default)
  {
    var provider = DbSchemaProviderFactory.Create(providerId);
    return await provider.TestConnectionAsync(connectionString, ct);
  }

  public string BuildTakeScript(ModuleInstanceConfig config, string tableName, int take = 100)
  {
    var entityName = EfScaffoldGenerator.ToPascalIdentifier(tableName);
    var ns = config.FullNamespace;
    return
      $"await using var db = new {ns}.AppDbContext();\n" +
      $"db.{entityName}s.Take({take}).Dump(\"{entityName}s\");";
  }

  public string BuildCountScript(ModuleInstanceConfig config, string tableName)
  {
    var entityName = EfScaffoldGenerator.ToPascalIdentifier(tableName);
    var ns = config.FullNamespace;
    return
      $"await using var db = new {ns}.AppDbContext();\n" +
      $"db.{entityName}s.Count().Dump(\"{entityName} count\");";
  }
}
