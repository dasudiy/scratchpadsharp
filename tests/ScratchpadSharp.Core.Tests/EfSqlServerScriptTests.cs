using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ScratchpadSharp.Core.Configuration;
using ScratchpadSharp.Core.Database;
using ScratchpadSharp.Core.External.NetPad.Presentation;
using ScratchpadSharp.Core.Modules;
using ScratchpadSharp.Core.PackageManagement;
using ScratchpadSharp.Core.Services;
using ScratchpadSharp.Shared.Models;

namespace ScratchpadSharp.Core.Tests;

public static class EfSqlServerScriptTests
{
    public static int RunAll()
    {
        AppConfiguration.Initialize();
        var failures = 0;
        failures += Run(nameof(EfSqlServer_QueryCompilation_DoesNotLoadSqlClientStubAsync),
            () => EfSqlServer_QueryCompilation_DoesNotLoadSqlClientStubAsync().GetAwaiter().GetResult());
        failures += Run(nameof(EfSqlServer_DumpEnumeration_DoesNotLoadSqlClientStubAsync),
            () => EfSqlServer_DumpEnumeration_DoesNotLoadSqlClientStubAsync().GetAwaiter().GetResult());
        failures += Run(nameof(RefreshMergedEnvironment_ReusesCachedPackageGraphAsync),
            () => RefreshMergedEnvironment_ReusesCachedPackageGraphAsync().GetAwaiter().GetResult());
        return failures;
    }

    private static int Run(string name, Func<bool> test) =>
        test() ? 0 : ReportFail(name);

    private static int ReportFail(string name)
    {
        Console.WriteLine($"FAIL: {name}");
        return 1;
    }

  /// <summary>
    /// Reproduces SqlServerConnection parsing connection string (no DB connection required).
    /// </summary>
    private static async Task<bool> EfSqlServer_QueryCompilation_DoesNotLoadSqlClientStubAsync()
    {
        var model = """
            using System;
            using Microsoft.EntityFrameworkCore;

            namespace Modules.HeadlessTest;

            public class Order
            {
                public int Id { get; set; }
            }

            public class AppDbContext : DbContext
            {
                public DbSet<Order> Orders => Set<Order>();
                protected override void OnConfiguring(DbContextOptionsBuilder options)
                    => options.UseSqlServer("Server=localhost;Database=test;TrustServerCertificate=True");
                protected override void OnModelCreating(ModelBuilder modelBuilder)
                    => modelBuilder.Entity<Order>().HasKey(e => e.Id);
            }
            """;

        var instance = new ModuleInstanceConfig
        {
            Id = "headless-test",
            DisplayName = "HeadlessTest",
            NamespaceSegment = "HeadlessTest",
            ProviderId = DatabaseProviderIds.SqlServer,
            ConnectionString = "Server=localhost;Database=test;TrustServerCertificate=True",
            Usings = ["System", "Microsoft.EntityFrameworkCore"]
        };
        DatabaseProviderCatalog.ApplyModulePackages(instance);
        ModuleCatalog.Instance.Save(instance, model);

        var tabId = Guid.NewGuid().ToString("N");
        var context = await ProjectService.Instance.CreateShellProjectAsync(tabId);
        context.Config.ModuleRefs = [instance.Id];
        await ProjectService.Instance.RefreshMergedEnvironmentAsync(tabId, context);

        var sqlClient = context.AbsoluteRuntimeReferences
            .FirstOrDefault(p => p.Contains("Microsoft.Data.SqlClient.dll", StringComparison.OrdinalIgnoreCase));
        if (sqlClient == null)
        {
            Console.WriteLine("SqlClient missing from runtime references");
            return false;
        }

        if (sqlClient.Contains("/ref/", StringComparison.OrdinalIgnoreCase) ||
            sqlClient.Contains("/lib/", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine($"SqlClient is not platform runtime: {sqlClient}");
            return false;
        }

        var code = """
            await using var db = new Modules.HeadlessTest.AppDbContext();
            var q = db.Orders.Take(1);
            using var e = q.GetEnumerator();
            """;

        var sink = new CollectDumpSink();
        var result = await new ScriptExecutionService().ExecuteAsync(code, context, sink);

        ModuleCatalog.Instance.Delete(instance.Id);
        RoslynWorkspaceService.Instance.RemoveProject(tabId);

        if (!result.Success)
        {
            Console.WriteLine($"Execution failed: {result.ErrorMessage}\n{result.Output}");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Reproduces O2Html enumerating EntityQueryable (Dump path).
    /// </summary>
    private static async Task<bool> EfSqlServer_DumpEnumeration_DoesNotLoadSqlClientStubAsync()
    {
        var model = """
            using System;
            using Microsoft.EntityFrameworkCore;

            namespace Modules.HeadlessDumpTest;

            public class Order
            {
                public int Id { get; set; }
            }

            public class AppDbContext : DbContext
            {
                public DbSet<Order> Orders => Set<Order>();
                protected override void OnConfiguring(DbContextOptionsBuilder options)
                    => options.UseSqlServer("Server=localhost;Database=test;TrustServerCertificate=True");
                protected override void OnModelCreating(ModelBuilder modelBuilder)
                    => modelBuilder.Entity<Order>().HasKey(e => e.Id);
            }
            """;

        var instance = new ModuleInstanceConfig
        {
            Id = "headless-dump-test",
            DisplayName = "HeadlessDumpTest",
            NamespaceSegment = "HeadlessDumpTest",
            ProviderId = DatabaseProviderIds.SqlServer,
            ConnectionString = "Server=localhost;Database=test;TrustServerCertificate=True",
            Usings = ["System", "Microsoft.EntityFrameworkCore"]
        };
        DatabaseProviderCatalog.ApplyModulePackages(instance);
        ModuleCatalog.Instance.Save(instance, model);

        var tabId = Guid.NewGuid().ToString("N");
        var context = await ProjectService.Instance.CreateShellProjectAsync(tabId);
        context.Config.ModuleRefs = [instance.Id];
        await ProjectService.Instance.RefreshMergedEnvironmentAsync(tabId, context);

        var code = """
            await using var db = new Modules.HeadlessDumpTest.AppDbContext();
            db.Orders.Take(1).Dump();
            """;

        var sink = new CollectDumpSink();
        var result = await new ScriptExecutionService().ExecuteAsync(code, context, sink);

        ModuleCatalog.Instance.Delete(instance.Id);
        RoslynWorkspaceService.Instance.RemoveProject(tabId);

        if (!result.Success)
        {
            Console.WriteLine($"Execution failed: {result.ErrorMessage}\n{result.Output}");
            return false;
        }

        if (sink.LastError != null)
        {
            Console.WriteLine($"Dump error: {sink.LastError}");
            return false;
        }

        return true;
    }

    private static async Task<bool> RefreshMergedEnvironment_ReusesCachedPackageGraphAsync()
    {
        var model = """
            using System;
            using Microsoft.EntityFrameworkCore;

            namespace Modules.CacheTest;

            public class Order
            {
                public int Id { get; set; }
            }

            public class AppDbContext : DbContext
            {
                public DbSet<Order> Orders => Set<Order>();
                protected override void OnConfiguring(DbContextOptionsBuilder options)
                    => options.UseSqlServer("Server=localhost;Database=test;TrustServerCertificate=True");
                protected override void OnModelCreating(ModelBuilder modelBuilder)
                    => modelBuilder.Entity<Order>().HasKey(e => e.Id);
            }
            """;

        var instance = new ModuleInstanceConfig
        {
            Id = "package-cache-test",
            DisplayName = "CacheTest",
            NamespaceSegment = "CacheTest",
            ProviderId = DatabaseProviderIds.SqlServer,
            ConnectionString = "Server=localhost;Database=test;TrustServerCertificate=True",
            Usings = ["System", "Microsoft.EntityFrameworkCore"]
        };
        DatabaseProviderCatalog.ApplyModulePackages(instance);
        ModuleCatalog.Instance.Save(instance, model);

        var tabId1 = Guid.NewGuid().ToString("N");
        var context1 = await ProjectService.Instance.CreateShellProjectAsync(tabId1);
        context1.Config.ModuleRefs = [instance.Id];
        await ProjectService.Instance.RefreshMergedEnvironmentAsync(tabId1, context1);

        var expected = context1.AbsoluteCompileReferences.ToList();
        if (expected.Count == 0)
        {
            Console.WriteLine("First resolve produced no compile references");
            return false;
        }

        var tabId2 = Guid.NewGuid().ToString("N");
        var context2 = await ProjectService.Instance.CreateShellProjectAsync(tabId2);
        context2.Config.ModuleRefs = [instance.Id];
        context2.Manifest = new PackageManifest();
        context2.AbsoluteCompileReferences.Clear();
        context2.AbsoluteRuntimeReferences.Clear();
        await ProjectService.Instance.RefreshMergedEnvironmentAsync(tabId2, context2);

        if (context2.AbsoluteCompileReferences.Count != expected.Count)
        {
            Console.WriteLine(
                $"Cached compile ref count mismatch: {context2.AbsoluteCompileReferences.Count} vs {expected.Count}");
            return false;
        }

        foreach (var path in expected)
        {
            if (!context2.AbsoluteCompileReferences.Contains(path, StringComparer.OrdinalIgnoreCase))
            {
                Console.WriteLine($"Missing cached compile ref: {path}");
                return false;
            }
        }

        if (context2.MergedEnvironment.ModuleSources.Count == 0)
        {
            Console.WriteLine("Cached refresh did not merge model.cs");
            return false;
        }

        return true;
    }

    private sealed class CollectDumpSink : IDumpSink
    {
        public string? LastError { get; private set; }

        public void ResultWrite<T>(T? value, DumpOptions? options = null)
        {
            if (value is string s && s.Contains("PlatformNotSupportedException", StringComparison.Ordinal))
                LastError = s;
        }

        public void SqlWrite<T>(T? value, DumpOptions? options = null) { }
    }
}
