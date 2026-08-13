using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ScratchpadSharp.Core.Database;
using ScratchpadSharp.Core.Security;
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

  public ModuleInstanceConfig CreateConfig(string displayName, string providerId, string connectionString,
    SshTunnelConfig? sshTunnel = null)
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
      SshTunnel = provider.SupportsSshTunnel ? sshTunnel : null,
      Usings = ["System", "Microsoft.EntityFrameworkCore"]
    };

    DatabaseProviderCatalog.ApplyModulePackages(config);
    return config;
  }

  public async Task<ModuleInstanceConfig> CreateInstanceAsync(string displayName, string providerId,
    string connectionString, SshTunnelConfig? sshTunnel = null, CancellationToken ct = default)
  {
    var config = CreateConfig(displayName, providerId, connectionString, sshTunnel);
    ModuleSecrets.ProtectInPlace(config);
    var provider = DatabaseProviderCatalog.Get(providerId);
    var schemaProvider = DbSchemaProviderFactory.Create(provider.Id);
    var snapshot = await WithLiveConnectionAsync(config, cs => schemaProvider.GetSchemaAsync(cs, ct), ct);
    var model = EfScaffoldGenerator.GenerateModel(snapshot, provider, config.NamespaceSegment, config.ConnectionString);
    ModuleCatalog.Instance.Save(config, model);
    return config;
  }

  public Task<ModuleInstanceConfig> UpdateConnectionAsync(ModuleInstanceConfig config, string providerId,
    string connectionString, SshTunnelConfig? sshTunnel = null, string? displayName = null,
    CancellationToken ct = default)
  {
    var provider = DatabaseProviderCatalog.Get(providerId);
    var oldCs = config.ConnectionString;
    var model = ModuleCatalog.Instance.ReadModelSource(config.Id) ?? string.Empty;

    if (!string.IsNullOrWhiteSpace(displayName))
      config.DisplayName = displayName.Trim();

    config.ProviderId = provider.Id;
    config.ConnectionString = connectionString;
    config.SshTunnel = provider.SupportsSshTunnel ? sshTunnel : null;
    DatabaseProviderCatalog.ApplyModulePackages(config);
    ModuleSecrets.ProtectInPlace(config);

    if (!string.IsNullOrEmpty(model) &&
        !string.Equals(oldCs, config.ConnectionString, StringComparison.Ordinal) &&
        EfScaffoldGenerator.TryReplaceBakedConnectionString(model, oldCs, config.ConnectionString, out var rewritten))
      model = rewritten;

    ModuleCatalog.Instance.Save(config, model);
    return Task.FromResult(config);
  }

  public async Task RegenerateModelAsync(string instanceId, CancellationToken ct = default)
  {
    var config = ModuleCatalog.Instance.TryGet(instanceId)
                 ?? throw new InvalidOperationException($"Module not found: {instanceId}");

    var provider = DatabaseProviderCatalog.Get(config.ProviderId);
    var schemaProvider = DbSchemaProviderFactory.Create(provider.Id);
    var snapshot = await WithLiveConnectionAsync(config, cs => schemaProvider.GetSchemaAsync(cs, ct), ct);
    var model = EfScaffoldGenerator.GenerateModel(snapshot, provider, config.NamespaceSegment, config.ConnectionString);
    ModuleCatalog.Instance.Save(config, model);
  }

  public async Task<DbSchemaSnapshot> GetSchemaAsync(string instanceId, CancellationToken ct = default)
  {
    var config = ModuleCatalog.Instance.TryGet(instanceId)
                 ?? throw new InvalidOperationException($"Module not found: {instanceId}");
    var provider = DbSchemaProviderFactory.Create(config.ProviderId);
    return await WithLiveConnectionAsync(config, cs => provider.GetSchemaAsync(cs, ct), ct);
  }

  public async Task<ConnectionTestResult> TestConnectionAsync(string providerId, string connectionString,
    SshTunnelConfig? sshTunnel = null, CancellationToken ct = default)
  {
    var config = new ModuleInstanceConfig
    {
      ProviderId = providerId,
      ConnectionString = connectionString,
      SshTunnel = sshTunnel
    };
    ModuleSecrets.ProtectInPlace(config);
    var provider = DbSchemaProviderFactory.Create(providerId);
    return await WithLiveConnectionAsync(config, cs => provider.TestConnectionAsync(cs, ct), ct);
  }

  private static async Task<T> WithLiveConnectionAsync<T>(
    ModuleInstanceConfig config, Func<string, Task<T>> action, CancellationToken ct)
  {
    var live = await ModuleSecrets.UnlockAsync(config, ct);
    await using var session = await SshTunnelSession.OpenIfNeededAsync(live, ct);
    var cs = session?.ConnectionString ?? live.ConnectionString;
    return await action(cs);
  }

  public string BuildTakeScript(ModuleInstanceConfig config, string tableName, int take = 100)
  {
    var entityName = EfScaffoldGenerator.ToPascalIdentifier(tableName);
    var ns = config.FullNamespace;
    return
      $"await using var db = new {ns}.AppDbContext();\n" +
      $"db.Set<{ns}.{entityName}>().Take({take}).Dump(\"{entityName}\");";
  }

  public string BuildCountScript(ModuleInstanceConfig config, string tableName)
  {
    var entityName = EfScaffoldGenerator.ToPascalIdentifier(tableName);
    var ns = config.FullNamespace;
    return
      $"await using var db = new {ns}.AppDbContext();\n" +
      $"db.Set<{ns}.{entityName}>().Count().Dump(\"{entityName} count\");";
  }
}
