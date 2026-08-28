using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NuGet.Frameworks;
using NuGet.Packaging.Core;
using NuGet.Versioning;
using ScratchpadSharp.Core.Configuration;
using ScratchpadSharp.Core.External.NetPad.Presentation;
using ScratchpadSharp.Core.Isolation;
using ScratchpadSharp.Core.PackageManagement;
using ScratchpadSharp.Core.Services;
using ScratchpadSharp.Core.Storage;
using ScratchpadSharp.Shared.Models;

namespace ScratchpadSharp.Core.Tests;

public static class ReferenceManagementTests
{
    private static readonly PackageIdentity JsonPackage =
        new("Newtonsoft.Json", NuGetVersion.Parse("13.0.3"));
    private static readonly PackageIdentity NodaTimePackage =
        new("NodaTime", NuGetVersion.Parse("3.2.2"));

    public static int RunAll()
    {
        AppConfiguration.Initialize();
        var failures = 0;
        failures += Run(nameof(AddReference_MissingFile_Throws), AddReference_MissingFile_Throws);
        failures += Run(nameof(AddReference_UnderProjectRoot_StoresRelativePathAndHydrates),
            () => AddReference_UnderProjectRoot_StoresRelativePathAndHydrates().GetAwaiter().GetResult());
        failures += Run(nameof(AddReference_OutsideProjectRoot_StillHydrates),
            () => AddReference_OutsideProjectRoot_StillHydrates().GetAwaiter().GetResult());
        failures += Run(nameof(AddReference_DuplicateSamePath_DoesNotDuplicate),
            () => AddReference_DuplicateSamePath_DoesNotDuplicate().GetAwaiter().GetResult());
        failures += Run(nameof(AddReference_SameFileNameDifferentFolders_ReplacesManifestId),
            () => AddReference_SameFileNameDifferentFolders_ReplacesManifestId().GetAwaiter().GetResult());
        failures += Run(nameof(AddReference_DoesNotRecordNativeAssets),
            () => AddReference_DoesNotRecordNativeAssets().GetAwaiter().GetResult());
        failures += Run(nameof(RemoveReference_ByFileName_ClearsConfigManifestAndHydrate),
            () => RemoveReference_ByFileName_ClearsConfigManifestAndHydrate().GetAwaiter().GetResult());
        failures += Run(nameof(BclNameInReferences_IsNotHydratedAsLocalFile),
            () => BclNameInReferences_IsNotHydratedAsLocalFile().GetAwaiter().GetResult());
        failures += Run(nameof(Script_CanCallTypeFromLocalDll),
            () => Script_CanCallTypeFromLocalDll().GetAwaiter().GetResult());
        failures += Run(nameof(SiblingDll_SameFolder_RunsWithoutExplicitAdd),
            () => SiblingDll_SameFolder_RunsWithoutExplicitAdd().GetAwaiter().GetResult());
        failures += Run(nameof(SiblingDll_LeakedType_SameFolder_CompilesAndRuns),
            () => SiblingDll_LeakedType_SameFolder_CompilesAndRuns().GetAwaiter().GetResult());
        failures += Run(nameof(SiblingDll_BothAdded_Runs),
            () => SiblingDll_BothAdded_Runs().GetAwaiter().GetResult());
        failures += Run(nameof(Alc_LoadBySimpleName_FindsLocalDll),
            () => Alc_LoadBySimpleName_FindsLocalDll().GetAwaiter().GetResult());
        failures += Run(nameof(Alc_ResolvesSiblingFromDirectory),
            () => Alc_ResolvesSiblingFromDirectory().GetAwaiter().GetResult());
        failures += Run(nameof(InferPackageRoot_ReturnsNullForLocalDll), InferPackageRoot_ReturnsNullForLocalDll);
        failures += Run(nameof(GetPhysicalPath_LocalAndNuGet), GetPhysicalPath_LocalAndNuGet);
        failures += Run(nameof(AddPackage_HydratesJsonCompileAndRuntime),
            () => AddPackage_HydratesJsonCompileAndRuntime().GetAwaiter().GetResult());
        failures += Run(nameof(RefreshMergedEnvironment_PreservesLocalAfterNuGetResolve),
            () => RefreshMergedEnvironment_PreservesLocalAfterNuGetResolve().GetAwaiter().GetResult());
        failures += Run(nameof(LocalDllDependingOnNodaTime_FailsUntilPackageAdded),
            () => LocalDllDependingOnNodaTime_FailsUntilPackageAdded().GetAwaiter().GetResult());
        failures += Run(nameof(LocalDll_WithDepsJson_ResolvesNuGetAtRuntime),
            () => LocalDll_WithDepsJson_ResolvesNuGetAtRuntime().GetAwaiter().GetResult());
        failures += Run(nameof(ContractsDll_ResolvesInfrastructureViaDepsJson),
            () => ContractsDll_ResolvesInfrastructureViaDepsJson().GetAwaiter().GetResult());
        failures += Run(nameof(FolderRoundTrip_RestoresLocalReference),
            () => FolderRoundTrip_RestoresLocalReference().GetAwaiter().GetResult());
        failures += Run(nameof(LoadProject_SelfHealsEmptyManifestFromLocalConfig),
            () => LoadProject_SelfHealsEmptyManifestFromLocalConfig().GetAwaiter().GetResult());
        failures += Run(nameof(ZipLoad_ExtractsPackedLocalDll),
            () => ZipLoad_ExtractsPackedLocalDll().GetAwaiter().GetResult());
        failures += Run(nameof(ApplySavedProjectState_HydratesAbsoluteLocalPath),
            () => ApplySavedProjectState_HydratesAbsoluteLocalPath().GetAwaiter().GetResult());
        failures += Run(nameof(DeletedLocalDll_RefreshDropsHydratedPath),
            () => DeletedLocalDll_RefreshDropsHydratedPath().GetAwaiter().GetResult());
        return failures;
    }

    private static int Run(string name, Func<bool> test)
    {
        try
        {
            if (test())
                return 0;
            Console.WriteLine($"FAIL: {name}");
            return 1;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAIL: {name}: {ex}");
            return 1;
        }
    }

    private static bool AddReference_MissingFile_Throws()
    {
        var tabId = Guid.NewGuid().ToString("N");
        var context = ProjectService.Instance.CreateShellProjectAsync(tabId).GetAwaiter().GetResult();
        try
        {
            ProjectService.Instance
                .AddReferenceAsync(tabId, context, Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.dll"))
                .GetAwaiter().GetResult();
            return false;
        }
        catch (FileNotFoundException)
        {
            return true;
        }
        finally
        {
            Cleanup(tabId, context.EffectiveRootPath);
        }
    }

    private static async Task<bool> AddReference_UnderProjectRoot_StoresRelativePathAndHydrates()
    {
        var (tabId, context) = await NewTabAsync();
        try
        {
            var (dll, ns) = CompileLibrary(Path.Combine(context.EffectiveRootPath, "libs"), "UnderRoot",
                "public static class Greeter { public static string Hello() => \"ok\"; }");
            await ProjectService.Instance.AddReferenceAsync(tabId, context, dll);

            if (context.Config.References.Count != 1)
                return Fail($"expected 1 config ref, got {context.Config.References.Count}");

            var stored = context.Config.References[0].Replace('\\', '/');
            if (Path.IsPathRooted(stored) || !stored.EndsWith("UnderRoot.dll", StringComparison.OrdinalIgnoreCase))
                return Fail($"expected relative config path, got {context.Config.References[0]}");

            var local = context.Manifest.ResolvedState.Assemblies
                .SingleOrDefault(a => a.Origin == AssetOrigin.Local);
            if (local == null || local.Id != "UnderRoot.dll" || local.RelativePath.Replace('\\', '/') != stored)
                return Fail($"manifest local mismatch: {local?.Id} {local?.RelativePath}");

            if (!ContainsPath(context.AbsoluteCompileReferences, dll) ||
                !ContainsPath(context.AbsoluteRuntimeReferences, dll))
                return Fail("hydrate missed compile or runtime path");

            return ns.Length > 0;
        }
        finally
        {
            Cleanup(tabId, context.EffectiveRootPath);
        }
    }

    private static async Task<bool> AddReference_OutsideProjectRoot_StillHydrates()
    {
        var (tabId, context) = await NewTabAsync();
        var outside = Directory.CreateTempSubdirectory("sps-ref-out-").FullName;
        try
        {
            var (dll, _) = CompileLibrary(outside, "OutsideLib",
                "public static class Greeter { public static string Hello() => \"out\"; }");
            await ProjectService.Instance.AddReferenceAsync(tabId, context, dll);

            if (context.Config.References.Count != 1)
                return Fail("config ref missing");
            if (!ContainsPath(context.AbsoluteCompileReferences, dll) ||
                !ContainsPath(context.AbsoluteRuntimeReferences, dll))
                return Fail($"outside dll not hydrated: {string.Join(';', context.AbsoluteCompileReferences)}");

            return true;
        }
        finally
        {
            Cleanup(tabId, context.EffectiveRootPath, outside);
        }
    }

    private static async Task<bool> AddReference_DuplicateSamePath_DoesNotDuplicate()
    {
        var (tabId, context) = await NewTabAsync();
        try
        {
            var (dll, _) = CompileLibrary(context.EffectiveRootPath, "DupLib",
                "public static class Greeter { public static string Hello() => \"dup\"; }");
            await ProjectService.Instance.AddReferenceAsync(tabId, context, dll);
            await ProjectService.Instance.AddReferenceAsync(tabId, context, dll);

            if (context.Config.References.Count != 1)
                return Fail($"config duplicated: {context.Config.References.Count}");
            if (context.Manifest.ResolvedState.Assemblies.Count(a => a.Origin == AssetOrigin.Local) != 1)
                return Fail("manifest duplicated");
            if (context.AbsoluteCompileReferences.Count(p => ContainsPath([p], dll)) != 1)
                return Fail("compile refs duplicated");

            return true;
        }
        finally
        {
            Cleanup(tabId, context.EffectiveRootPath);
        }
    }

    private static async Task<bool> AddReference_SameFileNameDifferentFolders_ReplacesManifestId()
    {
        var (tabId, context) = await NewTabAsync();
        var other = Directory.CreateTempSubdirectory("sps-ref-clash-").FullName;
        try
        {
            var (first, _) = CompileLibrary(context.EffectiveRootPath, "ClashLib",
                "public static class Greeter { public static string Hello() => \"first\"; }");
            var (second, _) = CompileLibrary(other, "ClashLib",
                "public static class Greeter { public static string Hello() => \"second\"; }");

            await ProjectService.Instance.AddReferenceAsync(tabId, context, first);
            await ProjectService.Instance.AddReferenceAsync(tabId, context, second);

            if (context.Config.References.Count != 2)
                return Fail($"expected 2 config paths, got {context.Config.References.Count}");

            var locals = context.Manifest.ResolvedState.Assemblies
                .Where(a => a.Origin == AssetOrigin.Local).ToList();
            if (locals.Count != 1 || locals[0].Id != "ClashLib.dll")
                return Fail($"expected single manifest id, got {locals.Count}");

            var hydrated = locals[0].RelativePath.Replace('\\', '/');
            if (!hydrated.Contains("ClashLib.dll", StringComparison.OrdinalIgnoreCase))
                return Fail($"unexpected manifest path {locals[0].RelativePath}");

            if (!ContainsPath(context.AbsoluteCompileReferences, second))
                return Fail("hydrate did not follow replaced path");

            return true;
        }
        finally
        {
            Cleanup(tabId, context.EffectiveRootPath, other);
        }
    }

    private static async Task<bool> AddReference_DoesNotRecordNativeAssets()
    {
        var (tabId, context) = await NewTabAsync();
        try
        {
            var (dll, _) = CompileLibrary(context.EffectiveRootPath, "NoNative",
                "public static class Greeter { public static string Hello() => \"n\"; }");
            await ProjectService.Instance.AddReferenceAsync(tabId, context, dll);

            return context.Manifest.ResolvedState.NativeAssets.Count == 0 &&
                   context.AbsoluteNativeAssets.Count == 0;
        }
        finally
        {
            Cleanup(tabId, context.EffectiveRootPath);
        }
    }

    private static async Task<bool> RemoveReference_ByFileName_ClearsConfigManifestAndHydrate()
    {
        var (tabId, context) = await NewTabAsync();
        try
        {
            EnableSave(context);
            var (dll, _) = CompileLibrary(context.EffectiveRootPath, "RemoveMe",
                "public static class Greeter { public static string Hello() => \"rm\"; }");
            await ProjectService.Instance.AddReferenceAsync(tabId, context, dll);
            await ProjectService.Instance.RemoveReferenceAsync(tabId, context, "RemoveMe.dll");

            if (context.Config.References.Count != 0)
                return Fail("config still has reference");
            if (context.Manifest.ResolvedState.Assemblies.Any(a => a.Origin == AssetOrigin.Local))
                return Fail("manifest still has local asset");
            if (ContainsPath(context.AbsoluteCompileReferences, dll) ||
                ContainsPath(context.AbsoluteRuntimeReferences, dll))
                return Fail("hydrate still lists removed dll");

            return true;
        }
        finally
        {
            Cleanup(tabId, context.EffectiveRootPath);
        }
    }

    private static async Task<bool> BclNameInReferences_IsNotHydratedAsLocalFile()
    {
        var (tabId, context) = await NewTabAsync();
        try
        {
            var (dll, _) = CompileLibrary(context.EffectiveRootPath, "WithBcl",
                "public static class Greeter { public static string Hello() => \"bcl\"; }");
            await ProjectService.Instance.AddReferenceAsync(tabId, context, dll);
            context.Config.References.Insert(0, "System.Runtime");
            await ProjectService.Instance.RefreshMergedEnvironmentAsync(tabId, context);

            if (context.Manifest.ResolvedState.Assemblies.Any(a =>
                    a.Origin == AssetOrigin.Local && a.Id.Equals("System.Runtime", StringComparison.OrdinalIgnoreCase)))
                return Fail("System.Runtime became a local asset");

            var locals = context.Manifest.ResolvedState.Assemblies
                .Where(a => a.Origin == AssetOrigin.Local).ToList();
            if (locals.Count != 1 || locals[0].Id != "WithBcl.dll")
                return Fail("real local dll lost during refresh");

            return ContainsPath(context.AbsoluteCompileReferences, dll);
        }
        finally
        {
            Cleanup(tabId, context.EffectiveRootPath);
        }
    }

    private static async Task<bool> Script_CanCallTypeFromLocalDll()
    {
        var (tabId, context) = await NewTabAsync();
        try
        {
            var (dll, ns) = CompileLibrary(context.EffectiveRootPath, "LeafRun",
                "public static class Greeter { public static string Hello() => \"hello-from-leaf\"; }");
            await ProjectService.Instance.AddReferenceAsync(tabId, context, dll);

            var result = await ExecuteAsync(context, $"System.Console.WriteLine({ns}.Greeter.Hello());");
            if (!result.Success)
                return Fail($"leaf script failed: {result.ErrorMessage}\n{result.Output}");
            return result.Output.Contains("hello-from-leaf", StringComparison.Ordinal);
        }
        finally
        {
            Cleanup(tabId, context.EffectiveRootPath);
        }
    }

    private static async Task<bool> SiblingDll_SameFolder_RunsWithoutExplicitAdd()
    {
        var (tabId, context) = await NewTabAsync();
        try
        {
            var libs = Path.Combine(context.EffectiveRootPath, "libs");
            var (utils, utilsNs) = CompileLibrary(libs, "LeafUtils",
                "public class UtilType { public string Id => \"utils\"; }");
            var (lib, libNs) = CompileLibrary(libs, "LeafLib",
                $"public static class Lib {{ public static string Get() => new {utilsNs}.UtilType().Id; }}",
                utils);
            await ProjectService.Instance.AddReferenceAsync(tabId, context, lib);

            var result = await ExecuteAsync(context, $"System.Console.WriteLine({libNs}.Lib.Get());");
            if (!result.Success)
                return Fail($"sibling runtime failed: {result.ErrorMessage}\n{result.Output}");

            return result.Output.Contains("utils", StringComparison.Ordinal);
        }
        finally
        {
            Cleanup(tabId, context.EffectiveRootPath);
        }
    }

    private static async Task<bool> SiblingDll_LeakedType_SameFolder_CompilesAndRuns()
    {
        var (tabId, context) = await NewTabAsync();
        try
        {
            var libs = Path.Combine(context.EffectiveRootPath, "libs");
            var (utils, utilsNs) = CompileLibrary(libs, "LeakUtils",
                "public class UtilType { public string Id => \"utils\"; }");
            var (lib, libNs) = CompileLibrary(libs, "LeakLib",
                $"public static class Lib {{ public static {utilsNs}.UtilType Create() => new(); }}",
                utils);
            await ProjectService.Instance.AddReferenceAsync(tabId, context, lib);

            if (!ContainsPath(context.AbsoluteCompileReferences, utils))
                return Fail("referenced sibling was not added as a compile reference");
            if (!CompileAndRuntimeNamesMatch(context))
                return Fail("sibling compile/runtime assembly names diverged");

            var result = await ExecuteAsync(context, $"var x = {libNs}.Lib.Create(); System.Console.WriteLine(x.Id);");
            if (!result.Success)
                return Fail($"leaked sibling type failed: {result.ErrorMessage}\n{result.Output}");

            return result.Output.Contains("utils", StringComparison.Ordinal);
        }
        finally
        {
            Cleanup(tabId, context.EffectiveRootPath);
        }
    }

    private static async Task<bool> SiblingDll_BothAdded_Runs()
    {
        var (tabId, context) = await NewTabAsync();
        try
        {
            var libs = Path.Combine(context.EffectiveRootPath, "libs");
            var (utils, utilsNs) = CompileLibrary(libs, "BothUtils",
                "public class UtilType { public string Id => \"utils-ok\"; }");
            var (lib, libNs) = CompileLibrary(libs, "BothLib",
                $"public static class Lib {{ public static {utilsNs}.UtilType Create() => new(); }}",
                utils);
            await ProjectService.Instance.AddReferenceAsync(tabId, context, lib);
            await ProjectService.Instance.AddReferenceAsync(tabId, context, utils);

            var result = await ExecuteAsync(context, $"System.Console.WriteLine({libNs}.Lib.Create().Id);");
            if (!result.Success)
                return Fail($"both-added run failed: {result.ErrorMessage}\n{result.Output}");

            return result.Output.Contains("utils-ok", StringComparison.Ordinal);
        }
        finally
        {
            Cleanup(tabId, context.EffectiveRootPath);
        }
    }

    private static async Task<bool> Alc_LoadBySimpleName_FindsLocalDll()
    {
        var (tabId, context) = await NewTabAsync();
        try
        {
            var (dll, ns) = CompileLibrary(context.EffectiveRootPath, "AlcLeaf",
                "public static class Greeter { public static string Hello() => \"alc\"; }");
            await ProjectService.Instance.AddReferenceAsync(tabId, context, dll);

            var alc = new ScriptAssemblyLoadContext(null, [], context.AbsoluteRuntimeReferences);
            try
            {
                var asm = alc.LoadFromAssemblyName(new AssemblyName(ns));
                var type = asm.GetType($"{ns}.Greeter");
                var hello = type?.GetMethod("Hello")?.Invoke(null, null) as string;
                return hello == "alc";
            }
            finally
            {
                alc.Unload();
            }
        }
        finally
        {
            Cleanup(tabId, context.EffectiveRootPath);
        }
    }

    private static async Task<bool> Alc_ResolvesSiblingFromDirectory()
    {
        var (tabId, context) = await NewTabAsync();
        try
        {
            var libs = Path.Combine(context.EffectiveRootPath, "libs");
            var (utils, utilsNs) = CompileLibrary(libs, "ProbeUtils",
                "public class UtilType { public string Id => \"u\"; }");
            var (lib, _) = CompileLibrary(libs, "ProbeLib",
                $"public static class Lib {{ public static string Get() => new {utilsNs}.UtilType().Id; }}",
                utils);
            await ProjectService.Instance.AddReferenceAsync(tabId, context, lib);

            var alc = new ScriptAssemblyLoadContext(null, [], context.AbsoluteRuntimeReferences);
            try
            {
                var asm = alc.LoadFromAssemblyName(new AssemblyName("ProbeUtils"));
                return asm.GetName().Name == "ProbeUtils";
            }
            finally
            {
                alc.Unload();
            }
        }
        finally
        {
            Cleanup(tabId, context.EffectiveRootPath);
        }
    }

    private static bool InferPackageRoot_ReturnsNullForLocalDll()
    {
        var dir = Directory.CreateTempSubdirectory("sps-ref-infer-").FullName;
        try
        {
            var (dll, _) = CompileLibrary(dir, "InferLib",
                "public static class Greeter { public static string Hello() => \"i\"; }");
            return NuGetPackageAssetResolver.InferPackageRoot(dll) == null;
        }
        finally
        {
            TryDelete(dir);
        }
    }

    private static bool GetPhysicalPath_LocalAndNuGet()
    {
        var local = PackageService.GetPhysicalPath(
            new ResolvedAsset { Origin = AssetOrigin.Local, Id = "A.dll", RelativePath = "libs/A.dll" },
            "/proj",
            "/cache");
        var nuget = PackageService.GetPhysicalPath(
            new ResolvedAsset
            {
                Origin = AssetOrigin.NuGet,
                Id = "Newtonsoft.Json",
                Version = "13.0.3",
                RelativePath = "lib/net6.0/Newtonsoft.Json.dll"
            },
            "/proj",
            "/cache");

        var localOk = local.Replace('\\', '/').EndsWith("/proj/libs/A.dll", StringComparison.Ordinal);
        var nugetOk = nuget.Replace('\\', '/').Contains("/cache/newtonsoft.json/13.0.3/", StringComparison.Ordinal) &&
                      nuget.Replace('\\', '/').EndsWith("Newtonsoft.Json.dll", StringComparison.Ordinal);
        return localOk && nugetOk;
    }

    private static async Task<bool> AddPackage_HydratesJsonCompileAndRuntime()
    {
        var (tabId, context) = await NewTabAsync();
        try
        {
            await ProjectService.Instance.AddPackageAsync(tabId, context, JsonPackage);

            var compile = context.AbsoluteCompileReferences
                .FirstOrDefault(p => p.Contains("Newtonsoft.Json.dll", StringComparison.OrdinalIgnoreCase));
            var runtime = context.AbsoluteRuntimeReferences
                .FirstOrDefault(p => p.Contains("Newtonsoft.Json.dll", StringComparison.OrdinalIgnoreCase));

            if (compile == null || runtime == null)
                return Fail("Newtonsoft.Json missing after AddPackage");
            if (runtime.Contains("/ref/", StringComparison.OrdinalIgnoreCase))
                return Fail($"runtime still a ref stub: {runtime}");
            if (!File.Exists(compile) || !File.Exists(runtime))
                return Fail("json assets missing on disk");

            var nugetAssets = context.Manifest.ResolvedState.Assemblies
                .Where(a => a.Origin == AssetOrigin.NuGet &&
                            a.Id.Equals("Newtonsoft.Json", StringComparison.OrdinalIgnoreCase))
                .ToList();
            return nugetAssets.Count > 0;
        }
        finally
        {
            Cleanup(tabId, context.EffectiveRootPath);
        }
    }

    private static async Task<bool> RefreshMergedEnvironment_PreservesLocalAfterNuGetResolve()
    {
        var (tabId, context) = await NewTabAsync();
        try
        {
            var (dll, _) = CompileLibrary(context.EffectiveRootPath, "KeepLocal",
                "public static class Greeter { public static string Hello() => \"keep\"; }");
            await ProjectService.Instance.AddReferenceAsync(tabId, context, dll);
            await ProjectService.Instance.AddPackageAsync(tabId, context, JsonPackage);

            if (!ContainsPath(context.AbsoluteCompileReferences, dll) ||
                !ContainsPath(context.AbsoluteRuntimeReferences, dll))
                return Fail("local dll dropped after NuGet resolve");

            if (!context.AbsoluteCompileReferences.Any(p =>
                    p.Contains("Newtonsoft.Json.dll", StringComparison.OrdinalIgnoreCase)))
                return Fail("json compile ref missing after mixed resolve");
            if (!CompileAndRuntimeNamesMatch(context))
                return Fail("mixed NuGet+local compile/runtime assembly names diverged");

            var local = context.Manifest.ResolvedState.Assemblies
                .Count(a => a.Origin == AssetOrigin.Local && a.Id == "KeepLocal.dll");
            return local == 1;
        }
        finally
        {
            Cleanup(tabId, context.EffectiveRootPath);
        }
    }

    private static async Task<bool> LocalDllDependingOnNodaTime_FailsUntilPackageAdded()
    {
        var (tabId, context) = await NewTabAsync();
        try
        {
            var nodaDll = await EnsurePackageCompileDllAsync(NodaTimePackage, "NodaTime.dll");
            var (dll, ns) = CompileLibrary(context.EffectiveRootPath, "NodaLeaf",
                """
                public static class NodaLib
                {
                    public static NodaTime.Instant Epoch() => NodaTime.Instant.FromUnixTimeSeconds(0);
                }
                """,
                nodaDll);

            await ProjectService.Instance.AddReferenceAsync(tabId, context, dll);

            if (context.Manifest.ResolvedState.Assemblies.Any(a =>
                    a.Origin == AssetOrigin.NuGet &&
                    a.Id.Equals("NodaTime", StringComparison.OrdinalIgnoreCase)))
                return Fail("local dll should not pull NuGet packages into the manifest");

            if (context.AbsoluteCompileReferences.Any(p =>
                    p.Contains("NodaTime.dll", StringComparison.OrdinalIgnoreCase)))
                return Fail("NodaTime should not be hydrated until it is a script package");

            var before = await ExecuteAsync(context,
                $"var x = {ns}.NodaLib.Epoch(); System.Console.WriteLine(x);");
            if (before.Success)
                return Fail("expected CS0012 when leaked NodaTime types are used without the package");
            if (!before.CompilationErrors.Any(e => e.Id == "CS0012") &&
                !$"{before.ErrorMessage}\n{before.Output}".Contains("CS0012", StringComparison.Ordinal))
                return Fail($"expected CS0012, got: {before.ErrorMessage}\n{before.Output}");

            await ProjectService.Instance.AddPackageAsync(tabId, context, NodaTimePackage);

            if (!ContainsPath(context.AbsoluteCompileReferences, dll))
                return Fail("local dll dropped when NodaTime was added");
            if (!context.AbsoluteRuntimeReferences.Any(p =>
                    p.Contains("NodaTime.dll", StringComparison.OrdinalIgnoreCase)))
                return Fail("NodaTime runtime assembly missing after AddPackage");

            var after = await ExecuteAsync(context,
                $"var x = {ns}.NodaLib.Epoch(); System.Console.WriteLine(x);");
            if (!after.Success)
                return Fail($"NodaTime package did not satisfy local dll: {after.ErrorMessage}\n{after.Output}");

            return after.Output.Contains("1970", StringComparison.Ordinal);
        }
        finally
        {
            Cleanup(tabId, context.EffectiveRootPath);
        }
    }

    private static async Task<bool> LocalDll_WithDepsJson_ResolvesNuGetAtRuntime()
    {
        var (tabId, context) = await NewTabAsync();
        try
        {
            var nodaDll = await EnsurePackageCompileDllAsync(NodaTimePackage, "NodaTime.dll");
            var (dll, ns) = CompileLibrary(context.EffectiveRootPath, "DepsLeaf",
                """
                public static class NodaLib
                {
                    public static string Epoch() => NodaTime.Instant.FromUnixTimeSeconds(0).ToString();
                }
                """,
                nodaDll);

            await File.WriteAllTextAsync(Path.ChangeExtension(dll, ".deps.json"),
                """
                {
                  "runtimeTarget": { "name": ".NETCoreApp,Version=v8.0", "signature": "" },
                  "targets": {
                    ".NETCoreApp,Version=v8.0": {
                      "DepsLeaf/1.0.0": {
                        "dependencies": { "NodaTime": "3.2.2" },
                        "runtime": { "DepsLeaf.dll": {} }
                      },
                      "NodaTime/3.2.2": {
                        "runtime": { "lib/net8.0/NodaTime.dll": { "assemblyVersion": "3.2.2.0", "fileVersion": "3.2.2.0" } }
                      }
                    }
                  },
                  "libraries": {
                    "DepsLeaf/1.0.0": { "type": "project", "serviceable": false, "sha512": "" },
                    "NodaTime/3.2.2": {
                      "type": "package",
                      "serviceable": true,
                      "sha512": "",
                      "path": "nodatime/3.2.2",
                      "hashPath": "nodatime.3.2.2.nupkg.sha512"
                    }
                  }
                }
                """);

            await ProjectService.Instance.AddReferenceAsync(tabId, context, dll);

            if (!context.AbsoluteCompileReferences.Any(p =>
                    p.Contains("NodaTime.dll", StringComparison.OrdinalIgnoreCase)))
                return Fail("deps.json did not add NodaTime as a compile reference");
            if (!context.AbsoluteRuntimeReferences.Any(p =>
                    p.Contains("NodaTime.dll", StringComparison.OrdinalIgnoreCase)))
                return Fail("deps.json did not add NodaTime to runtime references");
            if (!CompileAndRuntimeNamesMatch(context))
                return Fail("deps.json compile/runtime assembly names diverged");

            var result = await ExecuteAsync(context,
                $"var x = {ns}.NodaLib.Epoch(); System.Console.WriteLine(NodaTime.Instant.FromUnixTimeSeconds(0));");
            if (!result.Success)
                return Fail($"deps.json runtime resolve failed: {result.ErrorMessage}\n{result.Output}");

            return result.Output.Contains("1970", StringComparison.Ordinal);
        }
        finally
        {
            Cleanup(tabId, context.EffectiveRootPath);
        }
    }

    private static async Task<bool> ContractsDll_ResolvesInfrastructureViaDepsJson()
    {
        const string dll =
            "/projects/git.justdotrip.com/private/airv/src/AirV.ApiGateway.Contracts/bin/Debug/net8.0/AirV.ApiGateway.Contracts.dll";
        if (!File.Exists(dll) || !File.Exists(Path.ChangeExtension(dll, ".deps.json")))
            return true;

        var (tabId, context) = await NewTabAsync();
        try
        {
            await ProjectService.Instance.AddReferenceAsync(tabId, context, dll);
            if (!context.AbsoluteCompileReferences.Any(p =>
                    p.Contains("InfiniteRefactor.Infrastructure.dll", StringComparison.OrdinalIgnoreCase)))
                return Fail("InfiniteRefactor.Infrastructure missing from compile references");
            if (!context.AbsoluteRuntimeReferences.Any(p =>
                    p.Contains("InfiniteRefactor.Infrastructure.dll", StringComparison.OrdinalIgnoreCase)))
                return Fail("InfiniteRefactor.Infrastructure missing from runtime references");
            if (!CompileAndRuntimeNamesMatch(context))
                return Fail("Contracts.dll compile/runtime assembly names diverged");

            var alc = new ScriptAssemblyLoadContext(null, [], context.AbsoluteRuntimeReferences);
            try
            {
                var asm = alc.LoadFromAssemblyName(new AssemblyName("InfiniteRefactor.Infrastructure"));
                return asm.GetName().Name == "InfiniteRefactor.Infrastructure" &&
                       !asm.Location.Contains("/ref/", StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                alc.Unload();
            }
        }
        finally
        {
            Cleanup(tabId, context.EffectiveRootPath);
        }
    }

    private static async Task<bool> FolderRoundTrip_RestoresLocalReference()
    {
        var folder = Directory.CreateTempSubdirectory("sps-ref-folder-").FullName;
        var tabId = Guid.NewGuid().ToString("N");
        var loadId = Guid.NewGuid().ToString("N");
        string? shellTemp = null;
        try
        {
            var (dll, _) = CompileLibrary(Path.Combine(folder, "libs"), "FolderLeaf",
                "public static class Greeter { public static string Hello() => \"folder\"; }");

            var context = await ProjectService.Instance.CreateShellProjectAsync(tabId);
            shellTemp = context.EffectiveRootPath;
            context.Config.TimeoutSeconds = 30;
            context.SourcePath = folder;
            context.EffectiveRootPath = folder;
            await ProjectService.Instance.AddReferenceAsync(tabId, context, dll);

            var loaded = await ProjectService.Instance.LoadProjectAsync(loadId, folder);
            if (!loaded.Config.References.Any(r =>
                    Path.GetFileName(r).Equals("FolderLeaf.dll", StringComparison.OrdinalIgnoreCase)))
                return Fail("loaded config missing local ref");
            if (!ContainsPath(loaded.AbsoluteCompileReferences, dll) ||
                !ContainsPath(loaded.AbsoluteRuntimeReferences, dll))
                return Fail("loaded hydrate missed dll");

            return true;
        }
        finally
        {
            Cleanup(tabId, folder, shellTemp ?? "");
            Cleanup(loadId);
        }
    }

    private static async Task<bool> LoadProject_SelfHealsEmptyManifestFromLocalConfig()
    {
        var folder = Directory.CreateTempSubdirectory("sps-ref-heal-").FullName;
        var loadId = Guid.NewGuid().ToString("N");
        try
        {
            var (dll, _) = CompileLibrary(Path.Combine(folder, "libs"), "HealLeaf",
                "public static class Greeter { public static string Hello() => \"heal\"; }");
            var relative = Path.GetRelativePath(folder, dll).Replace('\\', '/');

            await PackageService.Instance.SaveAsync(new ScriptPackage
            {
                Code = "",
                Config = new ScriptConfig
                {
                    References = [relative],
                    TimeoutSeconds = 30
                },
                Manifest = new PackageManifest(),
                RootPath = folder
            }, folder);

            var loaded = await ProjectService.Instance.LoadProjectAsync(loadId, folder);
            var local = loaded.Manifest.ResolvedState.Assemblies
                .SingleOrDefault(a => a.Origin == AssetOrigin.Local);
            if (local == null || local.Id != "HealLeaf.dll")
                return Fail("self-heal did not restore local asset");

            return ContainsPath(loaded.AbsoluteCompileReferences, dll);
        }
        finally
        {
            Cleanup(loadId, folder);
        }
    }

    private static async Task<bool> ZipLoad_ExtractsPackedLocalDll()
    {
        var work = Directory.CreateTempSubdirectory("sps-ref-zip-").FullName;
        try
        {
            var (dll, _) = CompileLibrary(Path.Combine(work, "libs"), "ZipLeaf",
                "public static class Greeter { public static string Hello() => \"zip\"; }");
            var zipPath = Path.Combine(work, "pack.lqpkg");

            var manifest = new PackageManifest
            {
                ResolvedState = new ResolvedState
                {
                    Assemblies =
                    [
                        new ResolvedAsset
                        {
                            Origin = AssetOrigin.Local,
                            Id = "ZipLeaf.dll",
                            RelativePath = "libs/ZipLeaf.dll"
                        }
                    ]
                }
            };
            var config = new ScriptConfig { References = ["libs/ZipLeaf.dll"], TimeoutSeconds = 30 };

            using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                await WriteZipEntryAsync(archive, "manifest.json",
                    JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
                await WriteZipEntryAsync(archive, "config.json",
                    JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));
                await WriteZipEntryAsync(archive, "code.cs", "");
                archive.CreateEntryFromFile(dll, "libs/ZipLeaf.dll");
            }

            var package = await PackageService.Instance.LoadAsync(zipPath);
            var extracted = Path.Combine(package.RootPath, "libs", "ZipLeaf.dll");
            if (!File.Exists(extracted))
                return Fail($"zip local dll was not extracted to {extracted}");

            var extractedOk = package.Manifest.ResolvedState.Assemblies.Any(a =>
                a.Origin == AssetOrigin.Local && a.Id == "ZipLeaf.dll");
            TryDelete(package.RootPath);
            return extractedOk;
        }
        finally
        {
            TryDelete(work);
        }
    }

    private static async Task<bool> ApplySavedProjectState_HydratesAbsoluteLocalPath()
    {
        var (tabId, context) = await NewTabAsync();
        var restoreId = Guid.NewGuid().ToString("N");
        string? restoreRoot = null;
        try
        {
            var (dll, _) = CompileLibrary(context.EffectiveRootPath, "SessionLeaf",
                "public static class Greeter { public static string Hello() => \"session\"; }");

            var config = context.Config.Clone();
            config.References = [dll];
            var manifest = new PackageManifest
            {
                ResolvedState = new ResolvedState
                {
                    Assemblies =
                    [
                        new ResolvedAsset
                        {
                            Origin = AssetOrigin.Local,
                            Id = "SessionLeaf.dll",
                            RelativePath = dll
                        }
                    ]
                }
            };

            var restore = await ProjectService.Instance.CreateShellProjectAsync(restoreId);
            restoreRoot = restore.EffectiveRootPath;
            restore.Config.TimeoutSeconds = 30;
            await ProjectService.Instance.ApplySavedProjectStateAsync(restoreId, restore, config, manifest);

            return ContainsPath(restore.AbsoluteCompileReferences, dll) &&
                   ContainsPath(restore.AbsoluteRuntimeReferences, dll);
        }
        finally
        {
            Cleanup(tabId, context.EffectiveRootPath);
            Cleanup(restoreId, restoreRoot ?? "");
        }
    }

    private static async Task<bool> DeletedLocalDll_RefreshDropsHydratedPath()
    {
        var (tabId, context) = await NewTabAsync();
        try
        {
            var (dll, _) = CompileLibrary(context.EffectiveRootPath, "GoneLeaf",
                "public static class Greeter { public static string Hello() => \"gone\"; }");
            await ProjectService.Instance.AddReferenceAsync(tabId, context, dll);
            File.Delete(dll);
            await ProjectService.Instance.RefreshMergedEnvironmentAsync(tabId, context);

            return !ContainsPath(context.AbsoluteCompileReferences, dll) &&
                   !ContainsPath(context.AbsoluteRuntimeReferences, dll);
        }
        finally
        {
            Cleanup(tabId, context.EffectiveRootPath);
        }
    }

    private static async Task<(string tabId, ProjectContext context)> NewTabAsync()
    {
        var tabId = Guid.NewGuid().ToString("N");
        var context = await ProjectService.Instance.CreateShellProjectAsync(tabId);
        context.Config.TimeoutSeconds = 30;
        return (tabId, context);
    }

    private static void EnableSave(ProjectContext context) =>
        context.SourcePath = context.EffectiveRootPath;

    private static void Cleanup(string tabId, params string[] dirs)
    {
        try { RoslynWorkspaceService.Instance.RemoveProject(tabId); }
        catch { /* workspace may not have the tab */ }

        foreach (var dir in dirs)
            TryDelete(dir);
    }

    private static void TryDelete(string dir)
    {
        try
        {
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                Directory.Delete(dir, true);
        }
        catch
        {
            /* temp leftovers are acceptable */
        }
    }

    private static bool Fail(string message)
    {
        Console.WriteLine(message);
        return false;
    }

    private static bool CompileAndRuntimeNamesMatch(ProjectContext context)
    {
        var compile = AssemblySimpleNames(context.AbsoluteCompileReferences);
        var runtime = AssemblySimpleNames(context.AbsoluteRuntimeReferences);
        if (compile.SetEquals(runtime))
            return true;

        Console.WriteLine("compile-only: " + string.Join(", ", compile.Except(runtime, StringComparer.OrdinalIgnoreCase)));
        Console.WriteLine("runtime-only: " + string.Join(", ", runtime.Except(compile, StringComparer.OrdinalIgnoreCase)));
        return false;
    }

    private static HashSet<string> AssemblySimpleNames(IEnumerable<string> paths)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            try
            {
                var name = AssemblyName.GetAssemblyName(path).Name;
                if (!string.IsNullOrEmpty(name))
                    names.Add(name);
            }
            catch
            {
                // skip unreadable
            }
        }

        return names;
    }

    private static bool ContainsPath(IEnumerable<string> paths, string expected)
    {
        var full = Path.GetFullPath(expected);
        return paths.Any(p => Path.GetFullPath(p).Equals(full, StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<ScriptExecutionResult> ExecuteAsync(ProjectContext context, string statement)
    {
        context.Config.TimeoutSeconds = Math.Max(context.Config.TimeoutSeconds, 30);
        if (context.Config.Usings.Count == 0)
            context.Config.Usings = ["System"];

        return await new ScriptExecutionService().ExecuteAsync(statement, context, new CollectingSink());
    }

    private static async Task<string> EnsurePackageCompileDllAsync(PackageIdentity identity, string fileName)
    {
        var packagePath = await NuGetService.Instance.EnsurePackageDownloadedAsync(identity, CancellationToken.None);
        var assets = await NuGetService.Instance.GetPackageAssetsAsync(packagePath, NuGetFramework.Parse("net8.0"));
        var dll = assets.CompileReferences.FirstOrDefault(p =>
            p.EndsWith(fileName, StringComparison.OrdinalIgnoreCase));
        if (dll == null || !File.Exists(dll))
            throw new FileNotFoundException($"{identity.Id} compile asset missing", dll);
        return dll;
    }

    private static (string path, string ns) CompileLibrary(
        string directory, string assemblyName, string typeSource, params string[] extraRefs)
    {
        Directory.CreateDirectory(directory);
        var ns = assemblyName;
        var source = $$"""
            using System;
            namespace {{ns}}
            {
            {{typeSource}}
            }
            """;

        var tree = CSharpSyntaxTree.ParseText(source);
        var references = MetadataReferenceProvider.GetDefaultReferences().ToList();
        foreach (var extra in extraRefs)
            references.Add(MetadataReference.CreateFromFile(extra));

        var compilation = CSharpCompilation.Create(
            assemblyName,
            [tree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var path = Path.Combine(directory, assemblyName + ".dll");
        var emit = compilation.Emit(path);
        if (!emit.Success)
        {
            var errors = string.Join(Environment.NewLine,
                emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
            throw new InvalidOperationException($"Failed to compile {assemblyName}: {errors}");
        }

        return (path, ns);
    }

    private static async Task WriteZipEntryAsync(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        await using var stream = entry.Open();
        await using var writer = new StreamWriter(stream);
        await writer.WriteAsync(content);
    }

    private sealed class CollectingSink : IDumpSink
    {
        public void ResultWrite<T>(T? o, DumpOptions? options = null) { }
        public void SqlWrite<T>(T? o, DumpOptions? options = null) { }
    }
}
