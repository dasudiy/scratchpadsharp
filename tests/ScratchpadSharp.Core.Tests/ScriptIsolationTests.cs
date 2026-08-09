using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Frameworks;
using NuGet.Packaging.Core;
using NuGet.Versioning;
using ScratchpadSharp.Core.Database;
using ScratchpadSharp.Core.Isolation;
using ScratchpadSharp.Core.PackageManagement;
using ScratchpadSharp.Shared.Models;

namespace ScratchpadSharp.Core.Tests;

public static class ScriptIsolationTests
{
    public static int RunAll()
    {
        var failures = 0;
        failures += Run(nameof(ResolveEfSqlServer_HasUnixSqlClientRuntimeAsync), () => ResolveEfSqlServer_HasUnixSqlClientRuntimeAsync().GetAwaiter().GetResult());
        failures += Run(nameof(ScriptAlc_CanCreateSqlConnectionStringBuilderAsync), () => ScriptAlc_CanCreateSqlConnectionStringBuilderAsync().GetAwaiter().GetResult());
        return failures;
    }

    private static int Run(string name, Func<bool> test) =>
        test() ? 0 : ReportFail(name);

    private static int ReportFail(string name)
    {
        Console.WriteLine($"FAIL: {name}");
        return 1;
    }

    private static async Task<bool> ResolveEfSqlServer_HasUnixSqlClientRuntimeAsync()
    {
        var packageDto = new ScriptPackage
        {
            Config = new ScriptConfig
            {
                NuGetPackages = new Dictionary<string, string>
                {
                    [DatabaseProviderCatalog.EfCorePackageId] = DatabaseProviderCatalog.EfCorePackageVersion,
                    ["Microsoft.EntityFrameworkCore.SqlServer"] = DatabaseProviderCatalog.EfCorePackageVersion
                }
            },
            Manifest = new PackageManifest(),
            RootPath = Path.GetTempPath()
        };

        try
        {
            var graph = await DependencyResolver.Instance.ResolveFullGraphAsync(
                packageDto.Config.NuGetPackages.Select(kv =>
                    new PackageIdentity(kv.Key, NuGetVersion.Parse(kv.Value))).ToList(),
                NuGetFramework.Parse("net8.0"),
                CancellationToken.None);

            var context = new ProjectContext { EffectiveRootPath = packageDto.RootPath, Manifest = packageDto.Manifest };
            foreach (var identity in graph)
            {
                var packagePath = await NuGetService.Instance.EnsurePackageDownloadedAsync(identity, CancellationToken.None);
                var assets = await NuGetService.Instance.GetPackageAssetsAsync(packagePath, NuGetFramework.Parse("net8.0"));
                foreach (var absPath in assets.CompileReferences)
                {
                    var relPath = Path.GetRelativePath(packagePath, absPath).Replace('\\', '/');
                    context.Manifest.ResolvedState.Assemblies.Add(new ResolvedAsset
                    {
                        Id = identity.Id,
                        Version = identity.Version.ToString(),
                        Origin = AssetOrigin.NuGet,
                        RelativePath = relPath
                    });
                }
            }

            HydratePathsLikeProjectService(context);

            var sqlClientRuntime = context.AbsoluteRuntimeReferences
                .FirstOrDefault(p => p.Contains("Microsoft.Data.SqlClient.dll", StringComparison.OrdinalIgnoreCase));

            if (sqlClientRuntime == null)
            {
                Console.WriteLine("No SqlClient in AbsoluteRuntimeReferences");
                return false;
            }

            if (sqlClientRuntime.Contains("/ref/", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"SqlClient still ref: {sqlClientRuntime}");
                return false;
            }

            if (!sqlClientRuntime.Contains("/runtimes/", StringComparison.OrdinalIgnoreCase) &&
                !sqlClientRuntime.Contains("/lib/", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"Unexpected SqlClient path: {sqlClientRuntime}");
                return false;
            }

            return File.Exists(sqlClientRuntime);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Resolve failed: {ex.Message}");
            return false;
        }
    }

    private static async Task<bool> ScriptAlc_CanCreateSqlConnectionStringBuilderAsync()
    {
        var packageDto = new ScriptPackage
        {
            Config = new ScriptConfig
            {
                NuGetPackages = new Dictionary<string, string>
                {
                    ["Microsoft.EntityFrameworkCore.SqlServer"] = DatabaseProviderCatalog.EfCorePackageVersion
                }
            },
            Manifest = new PackageManifest(),
            RootPath = Path.GetTempPath()
        };

        try
        {
            var graph = await DependencyResolver.Instance.ResolveFullGraphAsync(
                packageDto.Config.NuGetPackages.Select(kv =>
                    new PackageIdentity(kv.Key, NuGetVersion.Parse(kv.Value))).ToList(),
                NuGetFramework.Parse("net8.0"),
                CancellationToken.None);

            var context = new ProjectContext { EffectiveRootPath = packageDto.RootPath, Manifest = packageDto.Manifest };
            foreach (var identity in graph)
            {
                var packagePath = await NuGetService.Instance.EnsurePackageDownloadedAsync(identity, CancellationToken.None);
                var assets = await NuGetService.Instance.GetPackageAssetsAsync(packagePath, NuGetFramework.Parse("net8.0"));
                foreach (var absPath in assets.CompileReferences)
                {
                    var relPath = Path.GetRelativePath(packagePath, absPath).Replace('\\', '/');
                    context.Manifest.ResolvedState.Assemblies.Add(new ResolvedAsset
                    {
                        Id = identity.Id,
                        Version = identity.Version.ToString(),
                        Origin = AssetOrigin.NuGet,
                        RelativePath = relPath
                    });
                }
            }

            HydratePathsLikeProjectService(context);

            var alc = new ScriptAssemblyLoadContext(null, [], context.AbsoluteRuntimeReferences);
            var asm = alc.LoadFromAssemblyName(new System.Reflection.AssemblyName("Microsoft.Data.SqlClient"));
            var builderType = asm.GetType("Microsoft.Data.SqlClient.SqlConnectionStringBuilder");
            if (builderType == null)
            {
                Console.WriteLine("SqlConnectionStringBuilder type missing");
                return false;
            }

            var builder = Activator.CreateInstance(builderType, "Server=localhost;Database=test");
            return builder != null && !asm.Location.Contains("/ref/", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ALC test failed: {ex}");
            return false;
        }
    }

    private static void HydratePathsLikeProjectService(ProjectContext context)
    {
        var settings = NuGet.Configuration.Settings.LoadDefaultSettings(null);
        var globalPackagesFolder = NuGet.Configuration.SettingsUtility.GetGlobalPackagesFolder(settings);

        context.AbsoluteCompileReferences.Clear();
        context.AbsoluteRuntimeReferences.Clear();

        foreach (var asset in context.Manifest.ResolvedState.Assemblies)
        {
            var absPath = Path.Combine(
                globalPackagesFolder,
                asset.Id.ToLowerInvariant(),
                asset.Version!.ToLowerInvariant(),
                asset.RelativePath.Replace('/', Path.DirectorySeparatorChar));

            if (!File.Exists(absPath))
                continue;

            context.AbsoluteCompileReferences.Add(absPath);
            context.AbsoluteRuntimeReferences.Add(NuGetPackageAssetResolver.ResolveRuntimeAssemblyPath(absPath));
        }
    }
}
