using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using ScratchpadSharp.Core.Database;
using ScratchpadSharp.Core.Modules;
using ScratchpadSharp.Shared.Models;

namespace ScratchpadSharp.Core.Tests;

public static class EfScaffoldGeneratorTests
{
    public static int RunAll()
    {
        var failures = 0;
        failures += Run(nameof(GeneratedModel_HasEfUsings), GeneratedModel_HasEfUsings);
        failures += Run(nameof(GeneratedModel_ConfiguresPrimaryKey), GeneratedModel_ConfiguresPrimaryKey);
        failures += Run(nameof(GeneratedModel_MapsToRealTableName), GeneratedModel_MapsToRealTableName);
        failures += Run(nameof(GeneratedModel_SkipsEfMigrationsHistory), GeneratedModel_SkipsEfMigrationsHistory);
        failures += Run(nameof(EnsureModuleUsings_PrependsMissing), EnsureModuleUsings_PrependsMissing);
        return failures;
    }

    private static int Run(string name, Func<bool> test) =>
        test() ? 0 : ReportFail(name);

    private static int ReportFail(string name)
    {
        Console.WriteLine($"FAIL: {name}");
        return 1;
    }

    private static bool GeneratedModel_HasEfUsings()
    {
        var snapshot = new DbSchemaSnapshot([
            new DbTableInfo("blogs", "main", false, [
                new DbColumnInfo("Id", "int", false, true, 0)
            ])
        ]);

        var provider = DatabaseProviderCatalog.Get(DatabaseProviderIds.Sqlite);
        var model = EfScaffoldGenerator.GenerateModel(snapshot, provider, "TestDb", "Data Source=test.db");

        return model.Contains("using Microsoft.EntityFrameworkCore;", StringComparison.Ordinal) &&
               model.Contains("using System;", StringComparison.Ordinal) &&
               model.Contains("public class AppDbContext : DbContext", StringComparison.Ordinal);
    }

    private static bool GeneratedModel_ConfiguresPrimaryKey()
    {
        var snapshot = new DbSchemaSnapshot([
            new DbTableInfo("orders", "main", false, [
                new DbColumnInfo("CustomerId", "int", false, true, 0),
                new DbColumnInfo("OrderId", "int", false, true, 1),
                new DbColumnInfo("Total", "decimal", true, false, 2)
            ])
        ]);

        var provider = DatabaseProviderCatalog.Get(DatabaseProviderIds.Sqlite);
        var model = EfScaffoldGenerator.GenerateModel(snapshot, provider, "TestDb", "Data Source=test.db");

        return model.Contains("HasKey(e => new { e.CustomerId, e.OrderId })", StringComparison.Ordinal);
    }

    private static bool GeneratedModel_MapsToRealTableName()
    {
        var snapshot = new DbSchemaSnapshot([
            new DbTableInfo("SalesTicketOrder", "dbo", false, [
                new DbColumnInfo("Id", "int", false, true, 0)
            ])
        ]);

        var provider = DatabaseProviderCatalog.Get(DatabaseProviderIds.SqlServer);
        var model = EfScaffoldGenerator.GenerateModel(
            snapshot, provider, "AirVDB", "Server=localhost;Database=test;TrustServerCertificate=True");

        return model.Contains("DbSet<SalesTicketOrder> SalesTicketOrders", StringComparison.Ordinal) &&
               model.Contains("entity.ToTable(\"SalesTicketOrder\", \"dbo\");", StringComparison.Ordinal);
    }

    private static bool GeneratedModel_SkipsEfMigrationsHistory()
    {
        var snapshot = new DbSchemaSnapshot([
            new DbTableInfo("__EFMigrationsHistory", "main", false, [
                new DbColumnInfo("MigrationId", "text", false, false, 0),
                new DbColumnInfo("ProductVersion", "text", false, false, 1)
            ]),
            new DbTableInfo("blogs", "main", false, [
                new DbColumnInfo("Id", "int", false, true, 0)
            ])
        ]);

        var provider = DatabaseProviderCatalog.Get(DatabaseProviderIds.Sqlite);
        var model = EfScaffoldGenerator.GenerateModel(snapshot, provider, "TestDb", "Data Source=test.db");

        return !model.Contains("EFMigrationsHistory", StringComparison.Ordinal) &&
               model.Contains("DbSet<Blogs>", StringComparison.Ordinal);
    }

    private static bool EnsureModuleUsings_PrependsMissing()
    {
        var source = "namespace Modules.Test;\npublic class AppDbContext : DbContext { }";
        var result = ModuleMergeService.EnsureModuleUsings(source, ["Microsoft.EntityFrameworkCore"]);

        return result.StartsWith("using Microsoft.EntityFrameworkCore;", StringComparison.Ordinal) &&
               result.Contains("namespace Modules.Test;", StringComparison.Ordinal);
    }
}
